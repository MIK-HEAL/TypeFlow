// Diagnostics for the isolated preview. No key injection or text insertion.
#include <windows.h>
#include <msctf.h>
#include <cstdio>
#include <cwchar>

static const GUID service = {0x5bb3e3a4,0xd9dc,0x4885,{0x9e,0xa4,0xc9,0x9b,0xd9,0x7a,0x9c,0x5c}};
static const GUID profile = {0xbe0c4c87,0x20c8,0x4e3b,{0xb7,0xdd,0x78,0x05,0x3f,0x19,0x4b,0x73}};

int wmain(int argc, wchar_t** argv) {
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) return 1;
    ITfInputProcessorProfileMgr* manager = nullptr;
    hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
                          IID_PPV_ARGS(&manager));
    if (FAILED(hr)) { CoUninitialize(); return 2; }
    const bool activate = argc == 2 && !wcscmp(argv[1], L"--activate");
    const bool restore = argc == 5 && !wcscmp(argv[1], L"--restore");
    if (activate || restore) {
        GUID targetService = service, targetProfile = profile;
        LANGID targetLanguage = 0x0804;
        if (restore) {
            if (FAILED(CLSIDFromString(argv[2], &targetService)) || FAILED(CLSIDFromString(argv[3], &targetProfile))) {
                manager->Release(); CoUninitialize(); return 5;
            }
            targetLanguage = static_cast<LANGID>(wcstoul(argv[4], nullptr, 16));
        }
        ITfInputProcessorProfiles* profiles = nullptr;
        hr = manager->QueryInterface(IID_PPV_ARGS(&profiles));
        if (SUCCEEDED(hr)) {
            hr = profiles->ChangeCurrentLanguage(targetLanguage);
            profiles->Release();
        }
        if (SUCCEEDED(hr)) hr = manager->ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR,
            targetLanguage, targetService, targetProfile, nullptr, TF_IPPMF_FORSESSION);
        std::printf("activate_hresult=0x%08lx\n", static_cast<unsigned long>(hr));
        if (hr != S_OK) { manager->Release(); CoUninitialize(); return 3; }
    }
    IEnumTfInputProcessorProfiles* enumerator = nullptr;
    hr = manager->EnumProfiles(0x0804, &enumerator);
    bool found = false;
    if (SUCCEEDED(hr)) {
        TF_INPUTPROCESSORPROFILE entry{};
        ULONG fetched = 0;
        while (enumerator->Next(1, &entry, &fetched) == S_OK) {
            if (IsEqualGUID(entry.clsid, service) && IsEqualGUID(entry.guidProfile, profile)) found = true;
        }
        enumerator->Release();
    }
    TF_INPUTPROCESSORPROFILE active{};
    hr = manager->GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD, &active);
    wchar_t clsid[40]{}, guid[40]{};
    StringFromGUID2(active.clsid, clsid, 40);
    StringFromGUID2(active.guidProfile, guid, 40);
    std::printf("registered=%s\nactive_hresult=0x%08lx\nactive_language=0x%04x\nactive_typeflow=%s\n",
        found ? "true" : "false", static_cast<unsigned long>(hr), active.langid,
        SUCCEEDED(hr) && IsEqualGUID(active.clsid, service) ? "true" : "false");
    std::wprintf(L"active_clsid=%ls\nactive_profile=%ls\n", clsid, guid);
    manager->Release();
    CoUninitialize();
    return found ? 0 : 4;
}
