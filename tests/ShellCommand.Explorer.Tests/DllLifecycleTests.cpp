#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <iostream>
#include <stdexcept>
#include <string>

// Intentionally does not include the implementation: exercise the actual exports,
// CRT, module references and system COM marshaler of the DLL that we distribute.
constexpr GUID kClass = {0xB8B3D4E2, 0x11A0, 0x4A5B, {0x8D, 0x2C, 0x7D, 0x0A, 0x0D, 0xB1, 0x01, 0x11}};
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
void Succeeded(HRESULT hr, const char* message) {
    if (FAILED(hr)) { std::cerr << message << ": 0x" << std::hex << hr << std::dec << '\n'; throw std::runtime_error(message); }
}
template<class T> struct Com {
    T* p = nullptr;
    Com() = default;
    Com(const Com&) = delete;
    Com& operator=(const Com&) = delete;
    ~Com() { if (p) p->Release(); }
    T* operator->() const { return p; }
};
void CheckIcon(IExplorerCommand* command, IShellItemArray* items) {
    LPWSTR icon = nullptr;
    const auto hr = command->GetIcon(items, &icon);
    // The observed Windows.UI.FileExplorer caller tests FAILED(hr), then reads
    // the returned UTF-16 string. Do not hide success-with-null behind `if(icon)`.
    if (SUCCEEDED(hr)) {
        Check(icon != nullptr, "successful GetIcon returned null");
        Check(hr == S_OK && icon[0] != L'\0', "invalid successful icon result");
        const std::wstring copied(icon);
        Check(!copied.empty(), "icon copy failed");
    } else {
        Check(hr == E_NOTIMPL && icon == nullptr, "missing icon must fail with an empty output");
    }
    CoTaskMemFree(icon);
}
void Exercise(IExplorerCommand* command, IShellItemArray* items) {
    Com<IObjectWithSite> site;
    Succeeded(command->QueryInterface(IID_PPV_ARGS(&site.p)), "IObjectWithSite");
    Succeeded(site->SetSite(nullptr), "clear site");
    CheckIcon(command, items);
    EXPCMDSTATE state{};
    const auto quick = command->GetState(items, FALSE, &state);
    Check(quick == E_PENDING || quick == S_OK, "fast GetState");
    Succeeded(command->GetState(items, TRUE, &state), "slow GetState");
    EXPCMDFLAGS flags{};
    Succeeded(command->GetFlags(&flags), "root flags");
    Check((flags & ECF_HASSUBCOMMANDS) != 0, "root lacks subcommands");
    Com<IEnumExplorerCommand> commands;
    Succeeded(command->EnumSubCommands(&commands.p), "EnumSubCommands");
    Com<IEnumExplorerCommand> clone;
    Succeeded(commands->Clone(&clone.p), "Clone");
    ULONG seen = 0;
    for (;;) {
        Com<IExplorerCommand> child;
        ULONG fetched = 99;
        const auto hr = commands->Next(1, &child.p, &fetched);
        if (hr == S_FALSE) { Check(!child.p && fetched == 0, "stale pointer at end"); break; }
        Succeeded(hr, "Next");
        Check(child.p && fetched == 1 && ++seen <= 128, "invalid enumeration");
        LPWSTR title = nullptr;
        Succeeded(child->GetTitle(items, &title), "child title");
        Check(title != nullptr, "null title");
        CoTaskMemFree(title);
        CheckIcon(child.p, items);
        GUID name{};
        Succeeded(child->GetCanonicalName(&name), "canonical name");
        Check(name != kClass, "child aliases root canonical name");
        Succeeded(child->GetFlags(&flags), "child flags");
        Succeeded(child->GetState(items, FALSE, &state), "child state");
    }
    Check(seen != 0, "missing fallback/menu");
    Succeeded(clone->Reset(), "Reset");
    Com<IExplorerCommand> first;
    // Optional fetched must also work through the real COM proxy.
    Succeeded(clone->Next(1, &first.p, nullptr), "optional fetched");
    Check(first.p != nullptr, "clone empty");
}
void Contexts(IExplorerCommand* command, IShellItemArray* items) {
    Exercise(command, nullptr); // no site / unavailable folder
    Exercise(command, items);   // real filesystem IShellItemArray
}
int wmain(int argc, wchar_t** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    try {
        Check(argc == 2, "usage: DllLifecycleTests <absolute DLL path | --registered>");
        Succeeded(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED), "CoInitializeEx");
        {
            wchar_t temporary[MAX_PATH]{};
            Check(GetTempPathW(MAX_PATH, temporary) != 0, "GetTempPath");
            Com<IShellItem> item;
            Succeeded(SHCreateItemFromParsingName(temporary, nullptr, IID_PPV_ARGS(&item.p)), "real folder item");
            Com<IShellItemArray> items;
            Succeeded(SHCreateShellItemArrayFromShellItem(item.p, IID_PPV_ARGS(&items.p)), "real selection array");
            if (std::wstring(argv[1]) == L"--registered") {
                for (int iteration = 0; iteration < 10; ++iteration) {
                    Com<IExplorerCommand> command;
                    Succeeded(CoCreateInstance(kClass, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&command.p)), "registered surrogate activation");
                    Contexts(command.p, items.p);
                }
                std::cout << "Registered surrogate activation and COM marshaling passed\n";
            } else {
                for (int iteration = 0; iteration < 100; ++iteration) {
                    const auto module = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                    Check(module != nullptr, "LoadLibraryEx");
                    using GetClass = HRESULT (STDAPICALLTYPE*)(REFCLSID, REFIID, void**);
                    using CanUnload = HRESULT (STDAPICALLTYPE*)();
                    const auto getClass = reinterpret_cast<GetClass>(GetProcAddress(module, "DllGetClassObject"));
                    const auto canUnload = reinterpret_cast<CanUnload>(GetProcAddress(module, "DllCanUnloadNow"));
                    Check(getClass && canUnload, "missing COM exports");
                    Check(canUnload() == S_OK, "fresh DLL cannot unload");
                    {
                        Com<IClassFactory> factory;
                        Succeeded(getClass(kClass, IID_PPV_ARGS(&factory.p)), "DllGetClassObject");
                        Check(canUnload() == S_FALSE, "factory not retaining module");
                        Com<IExplorerCommand> command;
                        Succeeded(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command.p)), "CreateInstance");
                        Contexts(command.p, items.p);
                    }
                    const auto deadline = GetTickCount64() + 5000;
                    while (canUnload() != S_OK && GetTickCount64() < deadline) Sleep(1);
                    Check(canUnload() == S_OK, "DLL references leaked");
                    Check(FreeLibrary(module) != FALSE, "FreeLibrary");
                    // The worker's FreeLibraryAndExitThread may trail its COM count.
                    while (GetModuleHandleW(argv[1]) && GetTickCount64() < deadline) Sleep(1);
                    Check(!GetModuleHandleW(argv[1]), "request worker retained module");
                }
                std::cout << "100 production DLL load / COM / unload cycles passed\n";
            }
        }
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
