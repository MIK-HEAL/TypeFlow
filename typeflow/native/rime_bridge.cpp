// Uses the public API in the existing BSD-licensed librime checkout.
// No theme or importer dependency belongs in this module.
#include "dynamic_library.h"
#include <rime_api.h>
#include <cstring>
#include <memory>
#include <string>
#include <stdexcept>
#include <mutex>

#ifdef _WIN32
#define TF_API extern "C" __declspec(dllexport)
#else
#define TF_API extern "C" __attribute__((visibility("default")))
#endif
namespace {
RimeApi* api = nullptr;
typeflow::LibraryHandle module = nullptr;
bool initialized = false;
std::mutex runtime_mutex;
std::string last_error;
struct Engine {
  std::string shared, user, schema;
  RimeSessionId session = 0;
  std::string result;
};
std::string quote(const char* value) {
  std::string result = "\"";
  if (value) for (const unsigned char c : std::string(value)) {
    switch (c) {
      case '"': result += "\\\""; break;
      case '\\': result += "\\\\"; break;
      case '\n': result += "\\n"; break;
      case '\r': result += "\\r"; break;
      case '\t': result += "\\t"; break;
      default:
        if (c < 32) {
          constexpr char hex[] = "0123456789abcdef";
          result += "\\u00"; result += hex[c >> 4]; result += hex[c & 15];
        } else result += static_cast<char>(c);
    }
  }
  return result + '"';
}
const char* snapshot(Engine* engine, bool handled) {
  RimeCommit commit{};
  RIME_STRUCT_INIT(RimeCommit, commit);
  std::string text;
  if (api->get_commit(engine->session, &commit)) {
    text = commit.text ? commit.text : "";
    api->free_commit(&commit);
  }
  std::string out = std::string("{\"handled\":") + (handled ? "true" : "false") +
      ",\"commit\":" + quote(text.c_str()) + ",\"view\":{";
  RimeContext context{};
  RIME_STRUCT_INIT(RimeContext, context);
  if (api->get_context(engine->session, &context)) {
    out += "\"preedit\":" + quote(context.composition.preedit) +
        ",\"highlighted\":" + std::to_string(context.menu.highlighted_candidate_index) +
        ",\"page\":" + std::to_string(context.menu.page_no) +
        ",\"isLastPage\":" + (context.menu.is_last_page ? "true" : "false") + ",\"candidates\":[";
    for (int i = 0; i < context.menu.num_candidates; ++i) {
      if (i) out += ',';
      const auto& candidate = context.menu.candidates[i];
      out += "{\"text\":" + quote(candidate.text) + ",\"comment\":" + quote(candidate.comment) +
          ",\"label\":" + quote(std::to_string(i + 1).c_str()) + "}";
    }
    out += ']';
    api->free_context(&context);
  } else out += "\"preedit\":\"\",\"candidates\":[]";
  engine->result = out + "}}";
  return engine->result.c_str();
}
}

TF_API const char* tf_error() noexcept { return last_error.c_str(); }
TF_API void* tf_open(const char* dll_path, const char* shared, const char* user, const char* schema) noexcept {
  std::lock_guard<std::mutex> lock(runtime_mutex);
  if (initialized) { last_error = "Only one Rime runtime per process is supported."; return nullptr; }
  try {
    auto engine = std::make_unique<Engine>();
    engine->shared = shared; engine->user = user; engine->schema = schema;
    module = typeflow::open_library(dll_path);
    const auto get_api = reinterpret_cast<RimeApi* (*)()>(typeflow::find_symbol(module, "rime_get_api"));
    if (!get_api) throw std::runtime_error("rime_get_api export missing.");
    api = get_api();
    if (!RIME_PROVIDED(api, select_candidate_on_current_page)) throw std::runtime_error("Unsupported librime API version.");
    RimeTraits traits{};
    RIME_STRUCT_INIT(RimeTraits, traits);
    traits.shared_data_dir = engine->shared.c_str();
    traits.user_data_dir = engine->user.c_str();
    traits.distribution_name = "TypeFlow";
    traits.distribution_code_name = "typeflow";
    traits.distribution_version = "0.1.0";
    traits.app_name = "rime.typeflow";
    traits.log_dir = engine->user.c_str();
    traits.min_log_level = 2;
    api->setup(&traits);
    api->initialize(&traits);
    initialized = true;
    if (api->start_maintenance(True)) api->join_maintenance_thread();
    engine->session = api->create_session();
    if (!engine->session || !api->select_schema(engine->session, engine->schema.c_str())) {
      if (engine->session) api->destroy_session(engine->session);
      throw std::runtime_error("Rime schema unavailable; deployment failed. Check the user directory logs.");
    }
    api->set_option(engine->session, "ascii_mode", False);
    api->set_option(engine->session, "simplification", True);
    return engine.release();
  } catch (const std::exception& e) {
    last_error = e.what();
    if (initialized && api) api->finalize();
    initialized = false; api = nullptr;
    typeflow::close_library(module);
    module = nullptr;
    return nullptr;
  }
}
TF_API const char* tf_key(void* handle, int key, int modifiers) noexcept {
  std::lock_guard<std::mutex> lock(runtime_mutex);
  try {
    auto* engine = static_cast<Engine*>(handle);
    return snapshot(engine, api->process_key(engine->session, key, modifiers) != 0);
  } catch (const std::exception& e) { last_error = e.what(); return nullptr; }
}
TF_API const char* tf_select(void* handle, int index) noexcept {
  std::lock_guard<std::mutex> lock(runtime_mutex);
  try {
    auto* engine = static_cast<Engine*>(handle);
    return snapshot(engine, api->select_candidate_on_current_page(engine->session, static_cast<size_t>(index)) != 0);
  } catch (const std::exception& e) { last_error = e.what(); return nullptr; }
}
TF_API void tf_clear(void* handle) noexcept {
  std::lock_guard<std::mutex> lock(runtime_mutex);
  api->clear_composition(static_cast<Engine*>(handle)->session);
}
TF_API void tf_close(void* handle) noexcept {
  std::lock_guard<std::mutex> lock(runtime_mutex);
  if (!handle) return;
  auto* engine = static_cast<Engine*>(handle);
  api->destroy_session(engine->session);
  api->finalize();
  delete engine;
  initialized = false; api = nullptr;
  typeflow::close_library(module);
  module = nullptr;
}
