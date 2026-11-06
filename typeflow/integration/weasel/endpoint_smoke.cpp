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
int main() {
  Context ctx{ {L"ni hao"}, {0, 0, true, {{L"你好"}, {L"拟好"}}, {}, {}} };
  auto& endpoint = typeflow::Endpoint::Instance();
  RECT rect{400, 400, 402, 426}; endpoint.Position(rect);
  for (int i = 0; i < 30; ++i) {
    endpoint.Update(ctx, true);
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
  }
  endpoint.Hide();
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  return 0;
}
