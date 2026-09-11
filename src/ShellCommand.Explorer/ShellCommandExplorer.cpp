#define WIN32_LEAN_AND_MEAN

#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <sddl.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlguid.h>
#include <servprov.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <limits>
#include <new>
#include <memory>
#include <string>
#include <utility>
#include <vector>

extern "C" IMAGE_DOS_HEADER __ImageBase;

// SDK 已声明同名原型，使用 linker alias 导出标准 COM 入口，避免重复定义冲突。
#pragma comment(linker, "/export:DllGetClassObject=ShellCommandGetClassObject,PRIVATE")
#pragma comment(linker, "/export:DllCanUnloadNow=ShellCommandCanUnloadNow,PRIVATE")

namespace {

// 协议常量必须与 docs/contracts/broker-ipc.md 保持一致。
constexpr std::uint16_t kProtocolVersion = 3;
constexpr std::uint16_t kResolveRequest = 10;
constexpr std::uint16_t kResolveResponse = 11;
constexpr std::uint16_t kInvokeRequest = 20;
constexpr std::uint16_t kInvokeResponse = 21;
constexpr std::uint32_t kMaxPayloadBytes = 256 * 1024;
constexpr std::uint32_t kMaxStringBytes = 32 * 1024;
constexpr std::uint16_t kMaxMenuItems = 128;
constexpr ULONGLONG kIpcDeadlineMs = 30;

constexpr GUID kClsid = {
    0xB8B3D4E2,
    0x11A0,
    0x4A5B,
    {0x8D, 0x2C, 0x7D, 0x0A, 0x0D, 0xB1, 0x01, 0x11},
};

std::atomic_ulong g_objectCount = 0;
std::atomic_ulong g_serverLocks = 0;
std::atomic_ulong g_pendingRequests = 0;
constexpr unsigned long kMaxPendingRequests = 8;
#ifdef SHELLCOMMAND_NATIVE_TEST
std::atomic_ulong g_testCleanupDelayMs = 0;
#endif

struct ChildData {
    std::wstring title;
    std::wstring icon;
    std::uint8_t token[16]{};
    bool isFallback = false;
    bool isSeparator = false;
    std::vector<ChildData> children;
};
struct SelectionData { std::wstring path; bool folder = false; };

class ScopedHandle final {
public:
    explicit ScopedHandle(HANDLE handle) noexcept : handle_(handle) {}
    ~ScopedHandle() noexcept {
        if (handle_ != nullptr && handle_ != INVALID_HANDLE_VALUE) {
            CloseHandle(handle_);
        }
    }

    ScopedHandle(const ScopedHandle&) = delete;
    ScopedHandle& operator=(const ScopedHandle&) = delete;

    HANDLE get() const noexcept { return handle_; }

private:
    HANDLE handle_ = nullptr;
};

void AppendUInt16(std::vector<std::uint8_t>& buffer, std::uint16_t value) {
    buffer.push_back(static_cast<std::uint8_t>(value));
    buffer.push_back(static_cast<std::uint8_t>(value >> 8));
}

void AppendUInt32(std::vector<std::uint8_t>& buffer, std::uint32_t value) {
    for (int shift = 0; shift < 32; shift += 8) {
        buffer.push_back(static_cast<std::uint8_t>(value >> shift));
    }
}

std::uint16_t ReadUInt16(const std::uint8_t* bytes) noexcept {
    return static_cast<std::uint16_t>(bytes[0] | (bytes[1] << 8));
}

std::uint32_t ReadUInt32(const std::uint8_t* bytes) noexcept {
    return static_cast<std::uint32_t>(bytes[0]) |
           (static_cast<std::uint32_t>(bytes[1]) << 8) |
           (static_cast<std::uint32_t>(bytes[2]) << 16) |
           (static_cast<std::uint32_t>(bytes[3]) << 24);
}

bool HasBytes(std::size_t position, std::size_t count, std::size_t size) noexcept {
    return position <= size && count <= size - position;
}

bool IsSc11Frame(const std::uint8_t* header, std::uint16_t messageType,
                std::uint32_t& payloadLength) noexcept {
    if (header[0] != 'S' || header[1] != 'C' || header[2] != '1' || header[3] != '1') {
        return false;
    }
    if (ReadUInt16(header + 4) != kProtocolVersion ||
        ReadUInt16(header + 6) != messageType) {
        return false;
    }

    payloadLength = ReadUInt32(header + 12);
    return payloadLength <= kMaxPayloadBytes;
}

// Called only by a bounded request worker. Completion owns its memory even when
// CancelIoEx is delayed. Never perform this cleanup wait on an Explorer callback.
struct PendingIo final {
    OVERLAPPED overlapped{};
    std::vector<std::uint8_t> bytes;
    explicit PendingIo(DWORD length) : bytes(length) {
        overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }
    ~PendingIo() { if (overlapped.hEvent) CloseHandle(overlapped.hEvent); }
};

bool TimedIo(HANDLE pipe, bool write, void* data, DWORD length,
             ULONGLONG deadline) {
    auto operation = std::make_unique<PendingIo>(length);
    if (!operation->overlapped.hEvent) return false;
    if (write && length) std::memcpy(operation->bytes.data(), data, length);
    DWORD position = 0;
    while (position < length) {
        if (GetTickCount64() >= deadline) return false;
        ResetEvent(operation->overlapped.hEvent);
        DWORD transferred = 0;
        const BOOL completed = write
            ? WriteFile(pipe, operation->bytes.data() + position, length - position,
                        &transferred, &operation->overlapped)
            : ReadFile(pipe, operation->bytes.data() + position, length - position,
                       &transferred, &operation->overlapped);
        if (!completed) {
            if (GetLastError() != ERROR_IO_PENDING) return false;
            const auto now = GetTickCount64();
            const auto waitMs = now >= deadline ? 0 : static_cast<DWORD>(deadline - now);
            if (WaitForSingleObject(operation->overlapped.hEvent, waitMs) != WAIT_OBJECT_0) {
                CancelIoEx(pipe, &operation->overlapped);
#ifdef SHELLCOMMAND_NATIVE_TEST
                Sleep(g_testCleanupDelayMs.load());
#endif
                // Even ERROR_NOT_FOUND from cancellation can race with completion.
                // The bounded worker retains the pipe and buffers until signaled.
                WaitForSingleObject(operation->overlapped.hEvent, INFINITE);
                GetOverlappedResult(pipe, &operation->overlapped, &transferred, FALSE);
                return false;
            }
            if (!GetOverlappedResult(pipe, &operation->overlapped, &transferred, FALSE)) return false;
        }
        if (transferred == 0) return false;
        position += transferred;
    }
    if (GetTickCount64() >= deadline) return false;
    if (!write && length) std::memcpy(data, operation->bytes.data(), length);
    return true;
}

std::wstring GetCurrentUserSid() noexcept {
    try {
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
            return L"unknown";
        }
        ScopedHandle tokenGuard(token);

        DWORD bufferSize = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &bufferSize);
        if (bufferSize == 0) {
            return L"unknown";
        }

        std::vector<std::uint8_t> buffer(bufferSize);
        if (!GetTokenInformation(token, TokenUser, buffer.data(), bufferSize, &bufferSize)) {
            return L"unknown";
        }

        LPWSTR sidText = nullptr;
        const auto* tokenUser = reinterpret_cast<const TOKEN_USER*>(buffer.data());
        if (!ConvertSidToStringSidW(tokenUser->User.Sid, &sidText)) {
            return L"unknown";
        }

        std::wstring sid(sidText);
        LocalFree(sidText);
        return sid;
    } catch (...) {
        // Explorer 侧无法记录或展示此错误，使用不会泄露用户信息的无效管道名。
        return L"unknown";
    }
}

HANDLE OpenBrokerPipe() {
    DWORD session = 0;
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &session)) return INVALID_HANDLE_VALUE;
#ifdef SHELLCOMMAND_NATIVE_TEST
    const std::wstring pipeName = L"\\\\.\\pipe\\ShellCommand11.Test." + std::to_wstring(GetCurrentProcessId());
#else
    const std::wstring pipeName = L"\\\\.\\pipe\\ShellCommand11." + GetCurrentUserSid() + L"." + std::to_wstring(session);
#endif
    return CreateFileW(
        pipeName.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION,
        nullptr);
}

bool ToUtf8(const std::wstring& value, std::vector<std::uint8_t>& output) {
    if (value.size() > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
        return false;
    }

    const int wideLength = static_cast<int>(value.size());
    const int byteCount = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        wideLength,
        nullptr,
        0,
        nullptr,
        nullptr);
    if (byteCount < 0 || byteCount > static_cast<int>(kMaxStringBytes)) {
        return false;
    }

    output.resize(static_cast<std::size_t>(byteCount));
    return byteCount == 0 ||
           WideCharToMultiByte(
               CP_UTF8,
               WC_ERR_INVALID_CHARS,
               value.data(),
               wideLength,
               reinterpret_cast<char*>(output.data()),
               byteCount,
               nullptr,
               nullptr) == byteCount;
}

bool ReadUtf8String(const std::vector<std::uint8_t>& payload, std::size_t& position,
                    std::wstring& result) {
    if (!HasBytes(position, sizeof(std::uint32_t), payload.size())) {
        return false;
    }

    const std::uint32_t byteLength = ReadUInt32(payload.data() + position);
    position += sizeof(std::uint32_t);
    if (byteLength > kMaxStringBytes ||
        !HasBytes(position, static_cast<std::size_t>(byteLength), payload.size())) {
        return false;
    }

    if (byteLength == 0) {
        result.clear();
        return true;
    }

    const auto* bytes = reinterpret_cast<const char*>(payload.data() + position);
    const int chars = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        bytes,
        static_cast<int>(byteLength),
        nullptr,
        0);
    if (chars <= 0) {
        return false;
    }

    result.resize(static_cast<std::size_t>(chars));
    if (MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            bytes,
            static_cast<int>(byteLength),
            result.data(),
            chars) != chars) {
        return false;
    }

    position += byteLength;
    return true;
}

bool ReadFrame(HANDLE pipe, std::uint16_t expectedType, std::uint32_t requestId, ULONGLONG deadline,
               std::vector<std::uint8_t>& payload) {
    std::uint8_t header[16]{};
    if (!TimedIo(pipe, false, header, sizeof(header), deadline)) {
        return false;
    }

    std::uint32_t payloadLength = 0;
    if (!IsSc11Frame(header, expectedType, payloadLength) || ReadUInt32(header + 8) != requestId) {
        return false;
    }

    payload.resize(payloadLength);
    return payload.empty() ||
           TimedIo(pipe, false, payload.data(), payloadLength, deadline);
}

std::vector<std::uint8_t> BuildFrame(std::uint16_t messageType,
                                     std::uint32_t requestId,
                                     const std::vector<std::uint8_t>& payload) {
    std::vector<std::uint8_t> frame;
    frame.reserve(16 + payload.size());
    frame.insert(frame.end(), {'S', 'C', '1', '1'});
    AppendUInt16(frame, kProtocolVersion);
    AppendUInt16(frame, messageType);
    AppendUInt32(frame, requestId);
    AppendUInt32(frame, static_cast<std::uint32_t>(payload.size()));
    frame.insert(frame.end(), payload.begin(), payload.end());
    return frame;
}

bool IsZeroToken(const std::uint8_t* token) noexcept {
    for (std::size_t i = 0; i < 16; ++i) {
        if (token[i] != 0) {
            return false;
        }
    }
    return true;
}

bool ReadMenuItems(const std::vector<std::uint8_t>& payload, std::size_t& position,
                   std::vector<ChildData>& children, unsigned int depth, unsigned int& total) {
    if (depth > 3 || !HasBytes(position, 2, payload.size())) return false;
    const auto count = ReadUInt16(payload.data() + position); position += 2;
    total += count;
    if (total > kMaxMenuItems) return false;
    children.clear(); children.reserve(count);
    for (std::uint16_t i = 0; i < count; ++i) {
        if (!HasBytes(position, 20, payload.size())) return false;
        ChildData child{};
        const auto kind = payload[position++]; const auto flags = payload[position++];
        const auto reserved = ReadUInt16(payload.data() + position); position += 2;
        std::memcpy(child.token, payload.data() + position, 16); position += 16;
        if (kind > 2 || flags != (kind == 1 ? 0 : 1) || reserved != 0 ||
            !ReadUtf8String(payload, position, child.title) || !ReadUtf8String(payload, position, child.icon) ||
            !ReadMenuItems(payload, position, child.children, depth + 1, total)) return false;
        child.isSeparator = kind == 1;
        if (kind == 1 && (!child.title.empty() || !child.icon.empty() || !IsZeroToken(child.token))) return false;
        if (kind == 0 && IsZeroToken(child.token)) return false;
        if (kind == 2 && (!IsZeroToken(child.token) || child.children.empty())) return false;
        if (kind != 2 && !child.children.empty()) return false;
        if (kind != 1 && child.title.empty()) return false;
        // User icons are prepared .ico files. Never pass arbitrary UNC/DLL references to the shell.
        if (!child.icon.empty() && (child.icon.starts_with(L"\\\\") ||
            child.icon.size() < 4 || _wcsicmp(child.icon.c_str() + child.icon.size() - 4, L".ico") != 0)) return false;
        children.push_back(std::move(child));
    }
    return true;
}
bool ParseResolveResponse(const std::vector<std::uint8_t>& payload, std::vector<ChildData>& children) {
    if (payload.size() < 3 || payload[0] > 1) return false;
    std::size_t position = 1; unsigned int total = 0;
    return ReadMenuItems(payload, position, children, 0, total) && position == payload.size();
}

bool ResolveCore(const std::wstring& directory, std::vector<ChildData>& children, ULONGLONG deadline, const std::vector<SelectionData>& selection) {
    ScopedHandle pipe(OpenBrokerPipe());
    if (pipe.get() == INVALID_HANDLE_VALUE) {
        return false;
    }

    std::vector<std::uint8_t> encodedPath;
    if (!ToUtf8(directory, encodedPath) ||
        encodedPath.size() > kMaxPayloadBytes - sizeof(std::uint32_t)) {
        return false;
    }

    std::vector<std::uint8_t> requestPayload;
    requestPayload.reserve(sizeof(std::uint32_t) + encodedPath.size());
    AppendUInt32(requestPayload, static_cast<std::uint32_t>(encodedPath.size()));
    requestPayload.insert(requestPayload.end(), encodedPath.begin(), encodedPath.end());
    if (selection.size() > 256) return false;
    AppendUInt16(requestPayload, static_cast<std::uint16_t>(selection.size()));
    for (const auto& item : selection) {
        std::vector<std::uint8_t> encoded;
        if (!ToUtf8(item.path, encoded)) return false;
        requestPayload.push_back(item.folder ? 1 : 0);
        AppendUInt32(requestPayload, static_cast<std::uint32_t>(encoded.size()));
        requestPayload.insert(requestPayload.end(), encoded.begin(), encoded.end());
        if (requestPayload.size() > kMaxPayloadBytes) return false;
    }


    const std::vector<std::uint8_t> frame =
        BuildFrame(kResolveRequest, 1, requestPayload);
    if (!TimedIo(
            pipe.get(),
            true,
            const_cast<std::uint8_t*>(frame.data()),
            static_cast<DWORD>(frame.size()),
            deadline)) {
        return false;
    }

    std::vector<std::uint8_t> responsePayload;
    if (!ReadFrame(pipe.get(), kResolveResponse, 1, deadline, responsePayload)) {
        return false;
    }
    return ParseResolveResponse(responsePayload, children) && GetTickCount64() < deadline;
}

bool InvokeTokenCore(const std::uint8_t token[16], ULONGLONG deadline) {
    ScopedHandle pipe(OpenBrokerPipe());
    if (pipe.get() == INVALID_HANDLE_VALUE) {
        return false;
    }

    std::vector<std::uint8_t> requestPayload(token, token + 16);
    const std::vector<std::uint8_t> frame =
        BuildFrame(kInvokeRequest, 2, requestPayload);
    if (!TimedIo(
            pipe.get(),
            true,
            const_cast<std::uint8_t*>(frame.data()),
            static_cast<DWORD>(frame.size()),
            deadline)) {
        return false;
    }

    std::vector<std::uint8_t> responsePayload;
    if (!ReadFrame(pipe.get(), kInvokeResponse, 2, deadline, responsePayload) ||
        responsePayload.size() != 1) {
        return false;
    }

    // 只有 ACCEPTED(0) 才表示 Broker 已接管动作；不等待实际进程执行结果。
    return responsePayload[0] == 0;
}

struct Request final {
    ScopedHandle ready{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    std::wstring directory;
    std::vector<SelectionData> selection;
    std::vector<ChildData> children;
    std::uint8_t token[16]{};
    ULONGLONG deadline = 0;
    HMODULE module = nullptr;
    bool invoke = false;
    bool success = false;
    std::atomic_bool completed = false;
};

DWORD WINAPI RequestWorker(void* context) noexcept {
    auto holder = std::unique_ptr<std::shared_ptr<Request>>(
        static_cast<std::shared_ptr<Request>*>(context));
    auto request = *holder;
    holder.reset();
    const HMODULE module = request->module;
    try {
        if (GetTickCount64() < request->deadline) {
            request->success = request->invoke
                ? InvokeTokenCore(request->token, request->deadline)
                : ResolveCore(request->directory, request->children, request->deadline, request->selection);
        }
    } catch (...) { request->success = false; }
    request->completed.store(true, std::memory_order_release);
    SetEvent(request->ready.get());
    request.reset();
    --g_pendingRequests;
    --g_objectCount;
    FreeLibraryAndExitThread(module, 0);
}

bool RunRequest(const std::shared_ptr<Request>& request) noexcept {
    auto pending = g_pendingRequests.load();
    do {
        if (pending >= kMaxPendingRequests) return false;
    } while (!g_pendingRequests.compare_exchange_weak(pending, pending + 1));
    ++g_objectCount;
    if (!request->ready.get() || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(&__ImageBase), &request->module)) {
        --g_pendingRequests; --g_objectCount; return false;
    }
    auto* holder = new (std::nothrow) std::shared_ptr<Request>(request);
    const HANDLE thread = holder ? CreateThread(nullptr, 0, RequestWorker, holder, 0, nullptr) : nullptr;
    if (!thread) {
        delete holder;
        FreeLibrary(request->module);
        --g_pendingRequests; --g_objectCount; return false;
    }
    CloseHandle(thread);
    const auto now = GetTickCount64();
    const auto waitMs = now >= request->deadline ? 0 : static_cast<DWORD>(request->deadline - now);
    if (WaitForSingleObject(request->ready.get(), waitMs) != WAIT_OBJECT_0) return false;
    return request->completed.load(std::memory_order_acquire) && request->success
        && GetTickCount64() < request->deadline;
}

bool Resolve(const std::wstring& directory, std::vector<ChildData>& children, const std::vector<SelectionData>& selection = {}) noexcept {
    const auto deadline = GetTickCount64() + kIpcDeadlineMs;
    try {
        if (directory.size() > kMaxStringBytes) return false;
        auto request = std::make_shared<Request>();
        request->deadline = deadline;
        request->directory = directory;
        request->selection = selection;
        if (!RunRequest(request)) return false;
        children = std::move(request->children);
        return true;
    } catch (...) { children.clear(); return false; }
}

bool InvokeToken(const std::uint8_t token[16]) noexcept {
    const auto deadline = GetTickCount64() + kIpcDeadlineMs;
    try {
        auto request = std::make_shared<Request>();
        request->deadline = deadline;
        request->invoke = true;
        std::memcpy(request->token, token, 16);
        return RunRequest(request);
    } catch (...) { return false; }
}

template<class T> class ComPtr final {
public:
    T* value = nullptr;
    ~ComPtr() { if (value) value->Release(); }
    T* operator->() const noexcept { return value; }
};
bool ItemPath(IShellItem* item, std::wstring& path) {
    PWSTR text = nullptr;
    if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &text)) || !text) return false;
    try { path = text; } catch (...) { CoTaskMemFree(text); throw; }
    CoTaskMemFree(text);
    return !path.empty() && path.size() <= kMaxStringBytes;
}
bool ViewDirectory(IUnknown* site, std::wstring& path) {
    if (!site) return false;
    ComPtr<IServiceProvider> provider;
    if (FAILED(site->QueryInterface(IID_PPV_ARGS(&provider.value)))) return false;
    ComPtr<IFolderView> view;
    if (FAILED(provider->QueryService(SID_SFolderView, IID_PPV_ARGS(&view.value)))) {
        ComPtr<IShellBrowser> browser;
        ComPtr<IShellView> shellView;
        if (FAILED(provider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser.value))) ||
            FAILED(browser->QueryActiveShellView(&shellView.value)) ||
            FAILED(shellView->QueryInterface(IID_PPV_ARGS(&view.value)))) return false;
    }
    ComPtr<IPersistFolder2> folder;
    if (FAILED(view->GetFolder(IID_PPV_ARGS(&folder.value)))) return false;
    PIDLIST_ABSOLUTE pidl = nullptr;
    if (FAILED(folder->GetCurFolder(&pidl))) return false;
    ComPtr<IShellItem> item;
    const auto hr = SHCreateItemFromIDList(pidl, IID_PPV_ARGS(&item.value));
    CoTaskMemFree(pidl);
    return SUCCEEDED(hr) && ItemPath(item.value, path);
}
bool CaptureContext(IUnknown* site, IShellItemArray* items, std::wstring& directory, std::vector<SelectionData>& selection) noexcept {
    try {
        directory.clear(); selection.clear();
        DWORD count = 0;
        if (items && FAILED(items->GetCount(&count))) return false;
        if (count == 0) return ViewDirectory(site, directory);
        if (count > 256) return false;
        for (DWORD i = 0; i < count; ++i) {
            ComPtr<IShellItem> item;
            if (FAILED(items->GetItemAt(i, &item.value))) return false;
            SFGAOF attributes = 0;
            if (FAILED(item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attributes)) || !(attributes & SFGAO_FILESYSTEM)) return false;
            SelectionData selected;
            if (!ItemPath(item.value, selected.path)) return false;
            selected.folder = (attributes & SFGAO_FOLDER) != 0;
            selection.push_back(std::move(selected));
        }
        if (selection.size() == 1 && selection[0].folder) { directory = selection[0].path; return true; }
        auto parent = [](const std::wstring& path) {
            const auto slash = path.find_last_of(L"\\/");
            if (slash == std::wstring::npos) return std::wstring{};
            return path.substr(0, slash == 2 && path[1] == L':' ? 3 : slash);
        };
        directory = parent(selection[0].path);
        for (const auto& item : selection)
            if (_wcsicmp(directory.c_str(), parent(item.path).c_str()) != 0) { directory.clear(); break; }
        // Search results may span directories: keep selection, leave directory absent.
        return true;
    } catch (...) { directory.clear(); selection.clear(); return false; }
}

HRESULT CopyStringToTaskMemory(const wchar_t* value, LPWSTR* output) noexcept {
    if (output == nullptr) {
        return E_POINTER;
    }
    *output = nullptr;
    if (value == nullptr) {
        return E_INVALIDARG;
    }

    const std::size_t charCount = std::wcslen(value) + 1;
    if (charCount > std::numeric_limits<std::size_t>::max() / sizeof(wchar_t)) {
        return E_OUTOFMEMORY;
    }

    const std::size_t byteCount = charCount * sizeof(wchar_t);
    auto* copy = static_cast<LPWSTR>(CoTaskMemAlloc(byteCount));
    if (copy == nullptr) {
        return E_OUTOFMEMORY;
    }

    std::memcpy(copy, value, byteCount);
    *output = copy;
    return S_OK;
}

class CommandEnumerator;

class ExplorerCommand final : public IExplorerCommand, public IObjectWithSite {
public:
    explicit ExplorerCommand(bool root) noexcept : isRoot_(root) {
        ++g_objectCount;
    }

    explicit ExplorerCommand(ChildData data) noexcept
        : data_(std::move(data)) {
        ++g_objectCount;
    }

    ~ExplorerCommand() noexcept {
        if (site_) site_->Release();
        --g_objectCount;
    }

    HRESULT QueryInterface(REFIID iid, void** result) noexcept override {
        if (result == nullptr) {
            return E_POINTER;
        }
        *result = nullptr;

        if (iid == IID_IUnknown || iid == IID_IExplorerCommand) {
            *result = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        if (iid == IID_IObjectWithSite) { *result = static_cast<IObjectWithSite*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }

    HRESULT SetSite(IUnknown* site) noexcept override {
        if (site) site->AddRef();
        if (site_) site_->Release();
        site_ = site; contextValid_ = false;
        return S_OK;
    }
    HRESULT GetSite(REFIID iid, void** value) noexcept override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return site_ ? site_->QueryInterface(iid, value) : E_FAIL;
    }
    ULONG AddRef() noexcept override {
        return ++referenceCount_;
    }

    ULONG Release() noexcept override {
        const ULONG remaining = --referenceCount_;
        if (remaining == 0) {
            delete this;
        }
        return remaining;
    }

    HRESULT GetTitle(IShellItemArray*, LPWSTR* title) noexcept override {
        return CopyStringToTaskMemory(
            isRoot_ ? L"ShellCommand" : data_.title.c_str(), title);
    }

    HRESULT GetIcon(IShellItemArray*, LPWSTR* icon) noexcept override {
        if (icon == nullptr) {
            return E_POINTER;
        }
        *icon = nullptr;

        // 根菜单使用安装包内稳定的系统图标；子项图标完全来自 Broker。
        const wchar_t* iconRef = isRoot_
            ? L"%SystemRoot%\\System32\\shell32.dll,-167"
            : data_.icon.c_str();
        if (*iconRef == L'\0') {
            return S_FALSE;
        }
        return CopyStringToTaskMemory(iconRef, icon);
    }

    HRESULT GetToolTip(IShellItemArray*, LPWSTR* value) noexcept override {
        if (value == nullptr) {
            return E_POINTER;
        }
        *value = nullptr;
        return E_NOTIMPL;
    }

    HRESULT GetCanonicalName(GUID* value) noexcept override {
        if (value == nullptr) {
            return E_POINTER;
        }
        *value = kClsid;
        if (!isRoot_ && !IsZeroToken(data_.token)) std::memcpy(value, data_.token, 16);
        return S_OK;
    }

    HRESULT GetState(IShellItemArray* items, BOOL fOkToBeSlow, EXPCMDSTATE* state) noexcept override {
        if (state == nullptr) {
            return E_POINTER;
        }

        *state = ECS_ENABLED;
        if (isRoot_) {
            if (!fOkToBeSlow) return E_PENDING;
            contextValid_ = CaptureContext(site_, items, currentDirectory_, selection_);
        }
        return S_OK;
    }

    HRESULT GetFlags(EXPCMDFLAGS* flags) noexcept override {
        if (flags == nullptr) {
            return E_POINTER;
        }
        *flags = (isRoot_ || !data_.children.empty())
            ? ECF_HASSUBCOMMANDS
            : (data_.isSeparator ? ECF_ISSEPARATOR : ECF_DEFAULT);
        return S_OK;
    }

    HRESULT Invoke(IShellItemArray*, IBindCtx*) noexcept override;
    HRESULT EnumSubCommands(IEnumExplorerCommand** result) noexcept override;

private:
    IUnknown* site_ = nullptr;
    bool contextValid_ = false;
    std::vector<SelectionData> selection_;
    ULONG referenceCount_ = 1;
    bool isRoot_ = false;
    ChildData data_{};
    std::vector<ChildData> children_;
    std::wstring currentDirectory_;
};

class CommandEnumerator final : public IEnumExplorerCommand {
public:
    explicit CommandEnumerator(std::vector<ChildData> items) noexcept
        : items_(std::move(items)) {
        ++g_objectCount;
    }

    ~CommandEnumerator() noexcept {
        --g_objectCount;
    }

    HRESULT QueryInterface(REFIID iid, void** result) noexcept override {
        if (result == nullptr) {
            return E_POINTER;
        }
        *result = nullptr;

        if (iid == IID_IUnknown || iid == IID_IEnumExplorerCommand) {
            *result = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    ULONG AddRef() noexcept override {
        return ++referenceCount_;
    }

    ULONG Release() noexcept override {
        const ULONG remaining = --referenceCount_;
        if (remaining == 0) {
            delete this;
        }
        return remaining;
    }

    HRESULT Next(ULONG count, IExplorerCommand** output,
                 ULONG* fetched) noexcept override {
        if (output == nullptr) {
            return E_POINTER;
        }
        if (count != 1 && fetched == nullptr) {
            return E_INVALIDARG;
        }

        ULONG copied = 0;
        try {
            while (copied < count && index_ < items_.size()) {
                output[copied] = new (std::nothrow) ExplorerCommand(items_[index_++]);
                if (output[copied] == nullptr) {
                    if (fetched != nullptr) {
                        *fetched = copied;
                    }
                    return E_OUTOFMEMORY;
                }
                ++copied;
            }
        } catch (...) {
            if (fetched != nullptr) {
                *fetched = copied;
            }
            return E_OUTOFMEMORY;
        }

        if (fetched != nullptr) {
            *fetched = copied;
        }
        return copied == count ? S_OK : S_FALSE;
    }

    HRESULT Skip(ULONG count) noexcept override {
        const std::size_t remaining = items_.size() - index_;
        index_ += static_cast<ULONG>(std::min<std::size_t>(count, remaining));
        return count <= remaining ? S_OK : S_FALSE;
    }

    HRESULT Reset() noexcept override {
        index_ = 0;
        return S_OK;
    }

    HRESULT Clone(IEnumExplorerCommand** result) noexcept override {
        if (result == nullptr) {
            return E_POINTER;
        }
        *result = nullptr;

        try {
            auto* clone = new (std::nothrow) CommandEnumerator(items_);
            if (clone == nullptr) {
                return E_OUTOFMEMORY;
            }
            clone->index_ = index_;
            *result = clone;
            return S_OK;
        } catch (...) {
            return E_OUTOFMEMORY;
        }
    }

private:
    ULONG referenceCount_ = 1;
    std::vector<ChildData> items_;
    std::size_t index_ = 0;
};

HRESULT ExplorerCommand::EnumSubCommands(IEnumExplorerCommand** result) noexcept {
    if (result == nullptr) {
        return E_POINTER;
    }
    *result = nullptr;
    if (!isRoot_ && data_.children.empty()) return E_NOTIMPL;

    try {
        children_.clear();
        if (!isRoot_) children_ = data_.children;
        else if (contextValid_) Resolve(currentDirectory_, children_, selection_);

        if (children_.empty()) {
            // Broker 不可用、路径不可用或响应非法时，始终保留最小可用入口。
            ChildData fallback;
            fallback.title = L"设置…";
            fallback.isFallback = true;
            children_.push_back(std::move(fallback));
        }

        auto* enumerator = new (std::nothrow) CommandEnumerator(std::move(children_));
        if (enumerator == nullptr) {
            return E_OUTOFMEMORY;
        }
        *result = enumerator;
        return S_OK;
    } catch (...) {
        return E_OUTOFMEMORY;
    }
}

HRESULT ExplorerCommand::Invoke(IShellItemArray*, IBindCtx*) noexcept {
    if (data_.isSeparator) {
        return E_FAIL;
    }

    if (isRoot_ || data_.isFallback) {
        // 这是唯一允许 Explorer DLL 直接启动的动作，路径固定为安装目录中的 App。
        wchar_t modulePath[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(
            reinterpret_cast<HMODULE>(&__ImageBase),
            modulePath,
            ARRAYSIZE(modulePath));
        if (length == 0 || length >= ARRAYSIZE(modulePath) ||
            !PathRemoveFileSpecW(modulePath) ||
            !PathAppendW(modulePath, L"ShellCommand.exe")) {
            return E_FAIL;
        }

        const HINSTANCE result = ShellExecuteW(
            nullptr,
            L"open",
            modulePath,
            nullptr,
            nullptr,
            SW_SHOWNORMAL);
        return reinterpret_cast<INT_PTR>(result) > 32 ? S_OK : E_FAIL;
    }

    return InvokeToken(data_.token) ? S_OK : E_FAIL;
}

class Factory final : public IClassFactory {
public:
    Factory() noexcept { ++g_objectCount; }
    ~Factory() noexcept { --g_objectCount; }
    HRESULT QueryInterface(REFIID iid, void** result) noexcept override {
        if (result == nullptr) {
            return E_POINTER;
        }
        *result = nullptr;

        if (iid == IID_IUnknown || iid == IID_IClassFactory) {
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    ULONG AddRef() noexcept override {
        return ++referenceCount_;
    }

    ULONG Release() noexcept override {
        const ULONG remaining = --referenceCount_;
        if (remaining == 0) {
            delete this;
        }
        return remaining;
    }

    HRESULT CreateInstance(IUnknown* outer, REFIID iid, void** result) noexcept override {
        if (outer != nullptr) {
            return CLASS_E_NOAGGREGATION;
        }
        if (result == nullptr) {
            return E_POINTER;
        }
        *result = nullptr;

        auto* command = new (std::nothrow) ExplorerCommand(true);
        if (command == nullptr) {
            return E_OUTOFMEMORY;
        }

        const HRESULT hr = command->QueryInterface(iid, result);
        command->Release();
        return hr;
    }

    HRESULT LockServer(BOOL lock) noexcept override {
        if (lock) {
            ++g_serverLocks;
        } else if (g_serverLocks != 0) {
            --g_serverLocks;
        }
        return S_OK;
    }

private:
    ULONG referenceCount_ = 1;
};

} // namespace

// Windows SDK 已声明这两个入口；使用 STDAPI 可匹配其 stdcall 与 C linkage。
extern "C" HRESULT STDAPICALLTYPE ShellCommandGetClassObject(
    REFCLSID clsid, REFIID iid, void** result) {
    if (result == nullptr) {
        return E_POINTER;
    }
    *result = nullptr;
    if (clsid != kClsid) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    try {
        auto* factory = new (std::nothrow) Factory();
        if (factory == nullptr) {
            return E_OUTOFMEMORY;
        }

        const HRESULT hr = factory->QueryInterface(iid, result);
        factory->Release();
        return hr;
    } catch (...) {
        // 不允许异常越过 DLL 导出函数 ABI。
        return E_UNEXPECTED;
    }
}

extern "C" HRESULT STDAPICALLTYPE ShellCommandCanUnloadNow(void) {
    return g_objectCount == 0 && g_serverLocks == 0 ? S_OK : S_FALSE;
}
