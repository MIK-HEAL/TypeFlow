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

namespace typeflow {
class Endpoint {
 public:
  static Endpoint& Instance() { static Endpoint endpoint; return endpoint; }
  Endpoint(const Endpoint&) = delete;
  Endpoint& operator=(const Endpoint&) = delete;
  ~Endpoint() {
    { std::lock_guard<std::mutex> lock(mutex_); stop_ = true; }
    changed_.notify_one();
    if (worker_.joinable()) worker_.join();
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
    {
      std::lock_guard<std::mutex> lock(mutex_);
      view_ = std::move(view); visible_ = visible; Queue();
    }
    changed_.notify_one();
    return visible && connected_.load();
  }
  void Position(const RECT& rect) {
    { std::lock_guard<std::mutex> lock(mutex_); rect_ = rect; Queue(); }
    changed_.notify_one();
  }
  void Hide() {
    { std::lock_guard<std::mutex> lock(mutex_); visible_ = false; Queue(); }
    changed_.notify_one();
  }
 private:
  Endpoint() : worker_([this] { Run(); }) {}
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
    pending_ = std::string("{\"visible\":") + (visible_ ? "true" : "false") +
        ",\"left\":" + std::to_string(rect_.left) + ",\"top\":" + std::to_string(rect_.top) +
        ",\"right\":" + std::to_string(rect_.right) + ",\"bottom\":" + std::to_string(rect_.bottom) +
        ",\"view\":" + view_ + "}\n";
    if (pending_.size() > 65536) pending_ = "{\"visible\":false}\n";
  }
  void Run() {
    DWORD session = 0; ProcessIdToSessionId(GetCurrentProcessId(), &session);
    const auto name = L"\\\\.\\pipe\\TypeFlow.Weasel.v1." + std::to_wstring(session);
    HANDLE pipe = INVALID_HANDLE_VALUE;
    while (true) {
      std::string message;
      {
        std::unique_lock<std::mutex> lock(mutex_);
        changed_.wait(lock, [this] { return stop_ || !pending_.empty(); });
        if (stop_) break;
        message.swap(pending_);
      }
      if (pipe == INVALID_HANDLE_VALUE)
        pipe = CreateFileW(name.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
      if (pipe == INVALID_HANDLE_VALUE) { connected_ = false; continue; }
      OVERLAPPED operation{};
      operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
      if (!operation.hEvent) { CloseHandle(pipe); pipe = INVALID_HANDLE_VALUE; connected_ = false; continue; }
      DWORD written = 0;
      BOOL success = WriteFile(pipe, message.data(), static_cast<DWORD>(message.size()), &written, &operation);
      if (!success && GetLastError() == ERROR_IO_PENDING) {
        if (WaitForSingleObject(operation.hEvent, 100) != WAIT_OBJECT_0) CancelIoEx(pipe, &operation);
        success = GetOverlappedResult(pipe, &operation, &written, TRUE);
      }
      CloseHandle(operation.hEvent);
      connected_ = success && written == message.size();
      if (!connected_) { CloseHandle(pipe); pipe = INVALID_HANDLE_VALUE; }
    }
    if (pipe != INVALID_HANDLE_VALUE) CloseHandle(pipe);
    connected_ = false;
  }
  std::mutex mutex_;
  std::condition_variable changed_;
  bool stop_ = false, visible_ = false;
  RECT rect_{};
  std::string view_ = "null", pending_;
  std::atomic<bool> connected_{false};
  std::thread worker_;
};
}
