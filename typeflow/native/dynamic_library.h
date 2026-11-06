#pragma once

#include <stdexcept>
#include <string>

#ifdef _WIN32
#include <windows.h>
#else
#include <dlfcn.h>
#endif

// Only this header knows how the OS loads native libraries. The bridge keeps its C ABI.
namespace typeflow {
#ifdef _WIN32
using LibraryHandle = HMODULE;
inline LibraryHandle open_library(const char* path) {
  const auto count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, nullptr, 0);
  if (!count) throw std::runtime_error("Invalid UTF-8 library path.");
  std::wstring wide(static_cast<size_t>(count), L'\0');
  MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, wide.data(), count);
  auto module = LoadLibraryExW(wide.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
  if (!module) throw std::runtime_error("Cannot load Rime library or a dependency. Win32 error " + std::to_string(GetLastError()));
  return module;
}
inline auto find_symbol(LibraryHandle module, const char* name) { return GetProcAddress(module, name); }
inline void close_library(LibraryHandle module) { if (module) FreeLibrary(module); }
#else
using LibraryHandle = void*;
inline LibraryHandle open_library(const char* path) {
  auto module = dlopen(path, RTLD_NOW | RTLD_LOCAL);
  if (!module) {
    const auto* error = dlerror();
    throw std::runtime_error(std::string("Cannot load Rime library: ") + (error ? error : "unknown loader error"));
  }
  return module;
}
inline auto find_symbol(LibraryHandle module, const char* name) { return dlsym(module, name); }
inline void close_library(LibraryHandle module) { if (module) dlclose(module); }
#endif
}
