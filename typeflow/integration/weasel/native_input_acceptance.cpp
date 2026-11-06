// A fresh ordinary Win32 RichEdit app. No librime calls and no injected keys.
// Keyboard input goes through Windows TSF and the installed text service.
#include <windows.h>
#include <richedit.h>
#include <msctf.h>
#include <atlbase.h>
#include <filesystem>
#include <fstream>
#include <string>

static HWND editor = nullptr;
static CComPtr<ITfThreadMgr> threads;
static CComPtr<ITfKeystrokeMgr> keys;
static TfClientId clientId = TF_CLIENTID_NULL;
static std::filesystem::path report;

static void record() {
    if (!editor) return;
    const int length = GetWindowTextLengthW(editor);
    std::wstring text(static_cast<size_t>(length) + 1, L'\0');
    GetWindowTextW(editor, text.data(), length + 1);
    bool chinese = false;
    for (wchar_t c : text) if (c >= 0x4e00 && c <= 0x9fff) chinese = true;
    std::ofstream output(report, std::ios::trunc);
    output << "{\"pid\":" << GetCurrentProcessId() << ",\"length\":" << length
           << ",\"containsChinese\":" << (chinese ? "true" : "false") << "}\n";
}

static HRESULT activate() {
    static const GUID service = {0x5bb3e3a4,0xd9dc,0x4885,{0x9e,0xa4,0xc9,0x9b,0xd9,0x7a,0x9c,0x5c}};
    static const GUID profile = {0xbe0c4c87,0x20c8,0x4e3b,{0xb7,0xdd,0x78,0x05,0x3f,0x19,0x4b,0x73}};
    CComPtr<ITfInputProcessorProfiles> profiles;
    HRESULT hr = profiles.CoCreateInstance(CLSID_TF_InputProcessorProfiles);
    if (FAILED(hr)) return hr;
    hr = profiles->ChangeCurrentLanguage(0x0804);
    if (FAILED(hr)) return hr;
    CComQIPtr<ITfInputProcessorProfileMgr> manager(profiles);
    if (!manager) return E_NOINTERFACE;
    return manager->ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, 0x0804,
        service, profile, nullptr, TF_IPPMF_FORPROCESS);
}

static LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM w, LPARAM l) {
    switch (message) {
    case WM_CREATE:
        CreateWindowW(L"STATIC", L"系统输入验收：输入 nihao，空格选词。此窗口仅使用 Windows TSF。",
            WS_CHILD | WS_VISIBLE, 16, 14, 720, 32, window, nullptr, nullptr, nullptr);
        editor = CreateWindowExW(WS_EX_CLIENTEDGE, MSFTEDIT_CLASS, L"",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_MULTILINE | ES_AUTOVSCROLL | WS_VSCROLL,
            16, 60, 730, 300, window, reinterpret_cast<HMENU>(100), nullptr, nullptr);
        SendMessageW(editor, EM_SETEDITSTYLE, SES_USECTF, SES_USECTF);
        SendMessageW(editor, EM_SETEVENTMASK, 0, ENM_CHANGE);
        SendMessageW(editor, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), TRUE);
        PostMessageW(window, WM_APP + 1, 0, 0);
        record(); return 0;
    case WM_APP + 1:
        SetFocus(editor); activate(); return 0;
    case WM_SIZE:
        if (editor) MoveWindow(editor, 16, 60, LOWORD(l) - 32, HIWORD(l) - 76, TRUE);
        return 0;
    case WM_COMMAND:
        if (HIWORD(w) == EN_CHANGE) record();
        break;
    case WM_DESTROY:
        record(); PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(window, message, w, l);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 1;
    wchar_t module[32768]{};
    GetModuleFileNameW(nullptr, module, ARRAYSIZE(module));
    report = std::filesystem::path(module).parent_path() / L"native-input-result.json";
    HMODULE rich = LoadLibraryW(L"Msftedit.dll");
    if (!rich || FAILED(threads.CoCreateInstance(CLSID_TF_ThreadMgr)) || FAILED(threads->Activate(&clientId))) return 2;
    threads->QueryInterface(IID_PPV_ARGS(&keys));
    WNDCLASSW cls{};
    cls.hInstance = instance; cls.lpfnWndProc = window_proc;
    cls.lpszClassName = L"TypeFlow.NativeInputAcceptance"; cls.hCursor = LoadCursor(nullptr, IDC_IBEAM);
    cls.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
    RegisterClassW(&cls);
    HWND window = CreateWindowW(cls.lpszClassName, L"TypeFlow · 系统输入验收", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 800, 450, nullptr, nullptr, instance, nullptr);
    ShowWindow(window, SW_SHOWNORMAL); UpdateWindow(window);
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        BOOL eaten = FALSE, tested = FALSE;
        if (keys && message.message == WM_KEYDOWN &&
            SUCCEEDED(keys->TestKeyDown(message.wParam, message.lParam, &tested)) && tested)
            keys->KeyDown(message.wParam, message.lParam, &eaten);
        else if (keys && message.message == WM_KEYUP &&
            SUCCEEDED(keys->TestKeyUp(message.wParam, message.lParam, &tested)) && tested)
            keys->KeyUp(message.wParam, message.lParam, &eaten);
        if (!eaten) { TranslateMessage(&message); DispatchMessageW(&message); }
    }
    keys.Release(); threads->Deactivate(); threads.Release();
    FreeLibrary(rich); CoUninitialize(); return 0;
}
