// Per-user initialization for the machine-installed IME. No text/key injection.
#include <windows.h>
#include <shlobj.h>
#include <shldisp.h>
#include <exdisp.h>
#include <servprov.h>
#include <msctf.h>
#include <atlbase.h>
#include <atlcomcli.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <stdexcept>

#ifndef TYPEFLOW_VERSION
#define TYPEFLOW_VERSION "development"
#endif
namespace fs = std::filesystem;

static fs::path app_directory() {
    wchar_t name[32768]{};
    if (!GetModuleFileNameW(nullptr, name, ARRAYSIZE(name))) throw std::runtime_error("module path");
    return fs::path(name).parent_path();
}

static fs::path user_directory() {
    wchar_t custom[32768]{};
    DWORD bytes = sizeof(custom);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\TypeFlow\\Ime", L"RimeUserDir",
            RRF_RT_REG_SZ, nullptr, custom, &bytes) == ERROR_SUCCESS && custom[0]) return custom;
    PWSTR path = nullptr;
    HRESULT hr = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_CREATE, nullptr, &path);
    if (FAILED(hr)) throw std::runtime_error("user directory");
    fs::path result = fs::path(path) / L"TypeFlow" / L"Ime";
    CoTaskMemFree(path);
    return result;
}

static void run(const fs::path& exe, const wchar_t* arguments, bool wait) {
    std::wstring command = L"\"" + exe.wstring() + L"\" " + arguments;
    STARTUPINFOW startup{sizeof(startup)};
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
            nullptr, exe.parent_path().c_str(), &startup, &process)) throw std::runtime_error("start process");
    CloseHandle(process.hThread);
    DWORD code = 0;
    if (wait) {
        if (WaitForSingleObject(process.hProcess, 120000) != WAIT_OBJECT_0 ||
                !GetExitCodeProcess(process.hProcess, &code)) code = 1;
    }
    CloseHandle(process.hProcess);
    if (code) throw std::runtime_error("deployment failed");
}

static bool has_deployed_schema(const fs::path& user) {
    const auto build = user / L"build";
    if (!fs::exists(build / L"default.yaml")) return false;
    for (const auto& entry : fs::directory_iterator(build)) {
        const auto name = entry.path().filename().wstring();
        const std::wstring suffix = L".schema.yaml";
        if (entry.is_regular_file() && name.size() >= suffix.size() &&
            name.compare(name.size() - suffix.size(), suffix.size(), suffix) == 0) return true;
    }
    return false;
}

// Ask the existing desktop shell to launch this same executable as its user.
// This also handles installers elevated with another administrator's account.
static HRESULT start_as_desktop_user(const fs::path& executable, bool enable, bool skinSettings) {
    CComPtr<IShellWindows> windows;
    HRESULT hr = windows.CoCreateInstance(CLSID_ShellWindows);
    if (FAILED(hr)) return hr;
    CComVariant empty;
    long desktop = 0;
    CComPtr<IDispatch> dispatch;
    hr = windows->FindWindowSW(&empty, &empty, SWC_DESKTOP, &desktop, SWFO_NEEDDISPATCH, &dispatch);
    if (hr != S_OK || !dispatch) return E_FAIL;
    CComQIPtr<IServiceProvider> provider(dispatch);
    if (!provider) return E_NOINTERFACE;
    CComPtr<IShellBrowser> browser;
    hr = provider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser));
    if (FAILED(hr)) return hr;
    CComPtr<IShellView> view;
    hr = browser->QueryActiveShellView(&view);
    if (FAILED(hr)) return hr;
    CComPtr<IDispatch> background;
    hr = view->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&background));
    if (FAILED(hr)) return hr;
    CComQIPtr<IShellFolderViewDual> folder(background);
    if (!folder) return E_NOINTERFACE;
    CComPtr<IDispatch> application;
    hr = folder->get_Application(&application);
    if (FAILED(hr)) return hr;
    CComQIPtr<IShellDispatch2> shell(application);
    if (!shell) return E_NOINTERFACE;
    const std::wstring arguments = std::wstring(enable ? L"--enable " : L"") + (skinSettings ? L"--skin-settings" : L"");
    return shell->ShellExecute(CComBSTR(executable.c_str()), CComVariant(arguments.c_str()),
        CComVariant(executable.parent_path().c_str()), CComVariant(L"open"), CComVariant(SW_SHOWNORMAL));
}

static void enable_profile() {
    static const GUID service = {0x5bb3e3a4,0xd9dc,0x4885,{0x9e,0xa4,0xc9,0x9b,0xd9,0x7a,0x9c,0x5c}};
    static const GUID profile = {0xbe0c4c87,0x20c8,0x4e3b,{0xb7,0xdd,0x78,0x05,0x3f,0x19,0x4b,0x73}};
    CComPtr<ITfInputProcessorProfileMgr> current;
    if (SUCCEEDED(current.CoCreateInstance(CLSID_TF_InputProcessorProfiles))) {
        TF_INPUTPROCESSORPROFILE active{};
        if (SUCCEEDED(current->GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD, &active)) &&
                IsEqualGUID(active.clsid, service) && IsEqualGUID(active.guidProfile, profile)) return;
    }
    CComPtr<ITfInputProcessorProfiles> profiles;
    HRESULT hr = profiles.CoCreateInstance(CLSID_TF_InputProcessorProfiles);
    if (FAILED(hr)) throw std::runtime_error("input profiles");
    hr = profiles->EnableLanguageProfile(service, 0x0804, profile, TRUE);
    if (FAILED(hr)) throw std::runtime_error("enable profile");
    hr = profiles->ChangeCurrentLanguage(0x0804);
    if (FAILED(hr)) throw std::runtime_error("input language");
    CComQIPtr<ITfInputProcessorProfileMgr> manager(profiles);
    if (!manager || manager->ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, 0x0804,
            service, profile, nullptr, TF_IPPMF_FORSESSION) != S_OK) throw std::runtime_error("activate profile");
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) return 1;
    int count = 0;
    PWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    bool desktopUser = false, enable = false, initializeOnly = false, settings = false, skinSettings = false, activateOnly = false;
    for (int i = 1; arguments && i < count; ++i) {
        if (!wcscmp(arguments[i], L"--as-user")) desktopUser = true;
        else if (!wcscmp(arguments[i], L"--enable")) enable = true;
        else if (!wcscmp(arguments[i], L"--initialize-only")) initializeOnly = true;
        else if (!wcscmp(arguments[i], L"--settings")) settings = true;
        else if (!wcscmp(arguments[i], L"--skin-settings")) skinSettings = true;
        else if (!wcscmp(arguments[i], L"--activate-only")) activateOnly = true;
        else { LocalFree(arguments); CoUninitialize(); return 2; }
    }
    LocalFree(arguments);
    int result = 0;
    HANDLE mutex = nullptr;
    try {
        const auto root = app_directory();
        if (activateOnly) {
            enable_profile();
        } else if (desktopUser) {
            if (FAILED(start_as_desktop_user(root / L"TypeFlowImeLauncher.exe", enable, skinSettings)))
                throw std::runtime_error("desktop shell");
        } else {
            const auto user = user_directory();
            const auto mutexName = L"Local\\TypeFlow.Ime.Initialize." + std::to_wstring(std::hash<std::wstring>{}(user.wstring()));
            mutex = CreateMutexW(nullptr, FALSE, mutexName.c_str());
            const DWORD acquired = mutex ? WaitForSingleObject(mutex, 120000) : WAIT_FAILED;
            if (acquired != WAIT_OBJECT_0 && acquired != WAIT_ABANDONED) throw std::runtime_error("initialization busy");
            fs::create_directories(user);
            const auto custom = user / L"default.custom.yaml";
            if (!fs::exists(custom) || fs::file_size(custom) == 0)
                fs::copy_file(root / L"data" / L"default.custom.yaml", custom, fs::copy_options::overwrite_existing);
            HKEY key = nullptr;
            if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\TypeFlow\\Ime", 0, nullptr, 0,
                    KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS) throw std::runtime_error("user settings");
            DWORD hant = 0;
            RegSetValueExW(key, L"Hant", 0, REG_DWORD, reinterpret_cast<BYTE*>(&hant), sizeof(hant));
            RegCloseKey(key);
            if (settings) {
                ShellExecuteW(nullptr, L"open", user.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
            } else {
                std::string installedVersion;
                { std::ifstream marker(user / L"typeflow-version.txt"); std::getline(marker, installedVersion); }
                if (installedVersion != TYPEFLOW_VERSION || !has_deployed_schema(user)) {
                    run(root / L"TypeFlowImeDeployer.exe", L"/deploy", true);
                    if (!has_deployed_schema(user)) throw std::runtime_error("missing input schema");
                    std::ofstream marker(user / L"typeflow-version.txt", std::ios::trunc);
                    marker << TYPEFLOW_VERSION << '\n';
                    if (!marker) throw std::runtime_error("deployment marker");
                }
                if (!initializeOnly) {
                    const auto skin = root / L"skin" / L"TypeFlow.Desktop.exe";
                    if (fs::exists(skin)) run(skin, L"--background", false);
                    run(root / L"TypeFlowImeServer.exe", L"", false);
                    if (enable) enable_profile();
                    if (skinSettings && fs::exists(skin)) run(skin, L"--settings", false);
                }
            }
        }
    } catch (const std::exception& error) {
        result = 1;
        std::wstring detail = L"TypeFlow 初始化未完成。\n请从开始菜单重新启动 TypeFlow，或打开用户配置检查部署。\n\n";
        for (const char* p = error.what(); *p; ++p) detail += static_cast<unsigned char>(*p);
        MessageBoxW(nullptr, detail.c_str(), L"TypeFlow", MB_OK | MB_ICONERROR);
    }
    if (mutex) { ReleaseMutex(mutex); CloseHandle(mutex); }
    CoUninitialize();
    return result;
}
