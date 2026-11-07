#include "TypeFlowEndpoint.h"
#include <vector>
#include <chrono>
struct Text { std::wstring str; };
struct Menu {
  int highlighted = 0, currentPage = 0;
  bool is_last_page = true;
  std::vector<Text> candies, comments, labels;
};
struct Context { Text preedit; Menu cinfo; };
int wmain(int argc, wchar_t** argv) {
  if (argc != 2) return 1;
  Context ctx{ {L"ni hao"}, {0, 0, true, {{L"你好"}, {L"拟好"}}, {}, {}} };
  typeflow::Endpoint endpoint("test", std::wstring(L"\\\\.\\pipe\\") + argv[1]);
  RECT rect{400, 400, 402, 426}; endpoint.Position(rect);
  if (!endpoint.Update(ctx, true)) return 2;
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
  while (!endpoint.Ready() && std::chrono::steady_clock::now() < deadline)
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  if (!endpoint.Ready()) return 3;
  endpoint.Hide();
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  return 0;
}
