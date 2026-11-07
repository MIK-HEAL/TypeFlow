#pragma once
// Optional presentation-only adapter for Weasel 0.17.4.
// Queueing and bounded asynchronous writes keep pipe I/O off the input thread.
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>
#include <functional>
#include <commctrl.h>
#include <chrono>

namespace typeflow {
class Endpoint {
 public:
  static Endpoint& Instance() { static Endpoint endpoint("legacy"); return endpoint; }
  explicit Endpoint(std::string source, std::wstring pipeName = {})
      : source_(std::move(source)), pipeName_(std::move(pipeName)), worker_([this] { Run(); }) {}
  bool Ready() const { return state_.load() == RenderState::Skin; }
  bool UseNative() const { return state_.load() == RenderState::Native; }
  Endpoint(const Endpoint&) = delete;
  Endpoint& operator=(const Endpoint&) = delete;
  ~Endpoint() {
    { std::lock_guard<std::mutex> lock(mutex_); stop_ = true; }
    changed_.notify_one();
    if (worker_.joinable()) worker_.join();
  }
  // All window callbacks execute on the native UI thread. The transport thread
  // only posts a notification after the host acknowledges a rendered frame.
  void Attach(HWND window, std::function<void()> refresh) {
    Detach(); refresh_ = std::move(refresh); window_ = window;
    SetWindowSubclass(window, WindowProc, 0x5446, reinterpret_cast<DWORD_PTR>(this));
  }
  void Detach() {
    HWND old = window_.exchange(nullptr);
    if (old) RemoveWindowSubclass(old, WindowProc, 0x5446);
    refresh_ = nullptr;
  }
  template<class Context> bool Update(const Context& ctx, bool composing) {
    const auto& menu = ctx.cinfo;
    std::string view = "{\"preedit\":" + Quote(ctx.preedit.str) +
        ",\"highlighted\":" + std::to_string(menu.highlighted) + ",\"page\":" + std::to_string(menu.currentPage) +
        ",\"isLastPage\":" + (menu.is_last_page ? "true" : "false") + ",\"candidates\":[";
    for (size_t i = 0; i < (std::min)(menu.candies.size(), size_t(10)); ++i) {
      if (i) view += ',';
      view += "{\"text\":" + Quote(menu.candies[i].str) + ",\"comment\":" +
          Quote(i < menu.comments.size() ? menu.comments[i].str : L"") + ",\"label\":" +
          Quote(i < menu.labels.size() ? menu.labels[i].str : std::to_wstring(i + 1)) + "}";
    }
    view += "]}";
    const bool visible = composing && view.size() <= 60000 && (!ctx.preedit.str.empty() || !menu.candies.empty());
    bool useSkin;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (visible != visible_) {
        ++generation_;
        state_ = visible ? RenderState::Pending : RenderState::Native;
      }
      view_ = std::move(view); visible_ = visible; Queue();
      useSkin = visible && !UseNative();
    }
    changed_.notify_one();
    return useSkin;
  }
  void Position(const RECT& rect) {
    { std::lock_guard<std::mutex> lock(mutex_); rect_ = rect; Queue(); }
    changed_.notify_one();
  }
  void Hide() {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (visible_) ++generation_;
      visible_ = false;
      state_ = RenderState::Native;
      Queue();
    }
    changed_.notify_one();
  }
 private:
  enum class RenderState { Native, Pending, Skin };
  static std::string Quote(const std::wstring& text) {
    const auto length = (std::min)(text.size(), size_t(4096));
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(length), nullptr, 0, nullptr, nullptr);
    std::string utf8(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(length), utf8.data(), size, nullptr, nullptr);
    std::string out = "\"";
    for (const unsigned char c : utf8) {
      if (c == '"' || c == '\\') { out += '\\'; out += static_cast<char>(c); }
      else if (c < 32) {
        constexpr char hex[] = "0123456789abcdef";
        out += "\\u00"; out += hex[c >> 4]; out += hex[c & 15];
      } else out += static_cast<char>(c);
    }
    return out + '"';
  }
  void Queue() {
    pending_ = std::string("{\"sourcePid\":") + std::to_string(GetCurrentProcessId()) +
        ",\"source\":\"" + source_ + "\",\"visible\":" + (visible_ ? "true" : "false") +
        ",\"left\":" + std::to_string(rect_.left) + ",\"top\":" + std::to_string(rect_.top) +
        ",\"right\":" + std::to_string(rect_.right) + ",\"bottom\":" + std::to_string(rect_.bottom) +
        ",\"view\":" + view_ + "}\n";
    if (pending_.size() > 65536) pending_ = "{\"visible\":false}\n";
  }
  static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l, UINT_PTR id, DWORD_PTR data) {
    auto* endpoint = reinterpret_cast<Endpoint*>(data);
    if (message == WM_APP + 0x544) { if (endpoint->refresh_) endpoint->refresh_(); return 0; }
    if (message == WM_NCDESTROY) { endpoint->window_ = nullptr; endpoint->refresh_ = nullptr; RemoveWindowSubclass(window, WindowProc, id); }
    return DefSubclassProc(window, message, w, l);
  }
  void SetState(RenderState value, unsigned generation) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (generation_ == generation && state_.exchange(value) != value) {
      if (HWND window = window_.load()) PostMessageW(window, WM_APP + 0x544, 0, 0);
    }
  }
  static bool Transfer(HANDLE pipe, void* data, DWORD size, bool read) {
    OVERLAPPED operation{};
    operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!operation.hEvent) return false;
    DWORD count = 0;
    BOOL success = read ? ReadFile(pipe, data, size, &count, &operation) : WriteFile(pipe, data, size, &count, &operation);
    if (!success && GetLastError() == ERROR_IO_PENDING) {
      if (WaitForSingleObject(operation.hEvent, 150) != WAIT_OBJECT_0) CancelIoEx(pipe, &operation);
      success = GetOverlappedResult(pipe, &operation, &count, TRUE);
    }
    CloseHandle(operation.hEvent);
    return success && count == size;
  }
  void Run() {
    DWORD session = 0; ProcessIdToSessionId(GetCurrentProcessId(), &session);
    const auto name = pipeName_.empty()
        ? L"\\\\.\\pipe\\TypeFlow.Candidates.v2." + std::to_wstring(session) : pipeName_;
    HANDLE pipe = INVALID_HANDLE_VALUE;
    std::string last;
    while (true) {
      std::string message;
      bool visible = false;
      unsigned generation;
      {
        std::unique_lock<std::mutex> lock(mutex_);
        changed_.wait_for(lock, std::chrono::milliseconds(250), [this] { return stop_ || !pending_.empty(); });
        if (stop_) break;
        message.swap(pending_);
        if (message.empty()) message = last;
        visible = visible_;
        generation = generation_;
      }
      if (message.empty()) continue;
      // An inactive input context must neither take the single renderer pipe
      // nor keep hiding a candidate window owned by another application.
      if (!visible) {
        if (pipe != INVALID_HANDLE_VALUE) {
          char acknowledged = 0;
          Transfer(pipe, message.data(), static_cast<DWORD>(message.size()), false);
          Transfer(pipe, &acknowledged, 1, true);
          CloseHandle(pipe); pipe = INVALID_HANDLE_VALUE;
        }
        last.clear(); continue;
      }
      last = message;
      if (pipe == INVALID_HANDLE_VALUE) {
        const auto open = [&] {
          return CreateFileW(name.c_str(), GENERIC_WRITE | GENERIC_READ, 0, nullptr, OPEN_EXISTING,
              FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
        };
        pipe = open();
        // Let the previous application finish its focus-loss handoff.
        if (pipe == INVALID_HANDLE_VALUE && GetLastError() == ERROR_PIPE_BUSY &&
            WaitNamedPipeW(name.c_str(), 40)) pipe = open();
      }
      if (pipe == INVALID_HANDLE_VALUE) { SetState(RenderState::Native, generation); continue; }
      char acknowledged = 0;
      const bool shown = Transfer(pipe, message.data(), static_cast<DWORD>(message.size()), false) &&
          Transfer(pipe, &acknowledged, 1, true) && acknowledged == '1';
      SetState(shown ? RenderState::Skin : RenderState::Native, generation);
      if (!shown) { CloseHandle(pipe); pipe = INVALID_HANDLE_VALUE; }
    }
    if (pipe != INVALID_HANDLE_VALUE) CloseHandle(pipe);
  }
  std::mutex mutex_;
  std::condition_variable changed_;
  bool stop_ = false, visible_ = false;
  RECT rect_{};
  std::string view_ = "null", pending_;
  unsigned generation_ = 0;
  std::atomic<RenderState> state_{RenderState::Native};
  std::atomic<HWND> window_{nullptr};
  std::function<void()> refresh_;
  std::string source_;
  std::wstring pipeName_;
  std::thread worker_;
};
}
