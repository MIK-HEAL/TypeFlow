#include <cstring>
#include <iostream>
#include <string>

#ifdef _WIN32
#define TF_IMPORT __declspec(dllimport)
#else
#define TF_IMPORT
#endif
extern "C" TF_IMPORT void* tf_open(const char*, const char*, const char*, const char*) noexcept;
extern "C" TF_IMPORT const char* tf_error() noexcept;

int main(int argc, char** argv) {
  if (argc != 2) return 2;
  const auto missing = std::string(argv[1]) + ".missing";
  if (tf_open(missing.c_str(), ".", ".", "unused") || std::strlen(tf_error()) == 0) return 1;
  // Loading this bridge itself succeeds, but it has no rime_get_api export.
  // Repeat after the first failure to verify native loader state is released.
  for (int attempt = 0; attempt < 2; ++attempt) {
    if (tf_open(argv[1], ".", ".", "unused") ||
        std::string(tf_error()) != "rime_get_api export missing.") return 1;
  }
  std::cout << "PASS: missing library, missing Rime export, retry after loader failure\n";
}
