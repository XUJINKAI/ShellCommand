#define SHELLCOMMAND_NATIVE_TEST
#include "../../src/ShellCommand.Explorer/ShellCommandExplorer.cpp"
#include <thread>
#include <iostream>
#include <stdexcept>

void Check(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

std::vector<std::uint8_t> Reply() {
    std::vector<std::uint8_t> payload{0, 2, 0};
    for (int item = 0; item < 2; ++item) {
        payload.insert(payload.end(), {0, 1, 0, 0});
        payload.insert(payload.end(), 16, static_cast<std::uint8_t>(item + 1));
        AppendUInt32(payload, 4);
        payload.insert(payload.end(), {'T', 'e', 's', 't'});
        AppendUInt32(payload, 0);
        AppendUInt16(payload, 0);
    }
    return BuildFrame(kResolveResponse, 1, payload);
}

enum class Fault { None, Slow, HalfHeader, Fragmented, Disconnect, WrongId, Oversize, WrongVersion };
void Exchange(Fault fault) {
    const auto name = L"\\\\.\\pipe\\ShellCommand11.Test." + std::to_wstring(GetCurrentProcessId());
    ScopedHandle pipe(CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
        PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr));
    Check(pipe.get() != INVALID_HANDLE_VALUE, "create fake broker");
    std::thread server([&] {
        ScopedHandle connected(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        OVERLAPPED connect{};
        connect.hEvent = connected.get();
        if (!ConnectNamedPipe(pipe.get(), &connect) && GetLastError() != ERROR_PIPE_CONNECTED) {
            if (GetLastError() != ERROR_IO_PENDING) return;
            if (WaitForSingleObject(connected.get(), 1500) != WAIT_OBJECT_0) {
                CancelIoEx(pipe.get(), &connect);
                WaitForSingleObject(connected.get(), INFINITE);
                return;
            }
            DWORD count = 0;
            if (!GetOverlappedResult(pipe.get(), &connect, &count, FALSE)) return;
        }
        const auto serverDeadline = GetTickCount64() + 1500;
        std::vector<std::uint8_t> input;
        if (!ReadFrame(pipe.get(), kResolveRequest, 1, serverDeadline, input)) return;
        auto reply = Reply();
        if (fault == Fault::Disconnect) { DisconnectNamedPipe(pipe.get()); return; }
        if (fault == Fault::Slow) Sleep(100);
        if (fault == Fault::WrongId) reply[8] = 99;
        if (fault == Fault::WrongVersion) reply[4] = 1;
        if (fault == Fault::Oversize) reply[15] = 127;
        if (fault == Fault::HalfHeader) {
            TimedIo(pipe.get(), true, reply.data(), 5, serverDeadline);
            Sleep(100);
        } else if (fault == Fault::Fragmented) {
            for (auto byte : reply) {
                if (!TimedIo(pipe.get(), true, &byte, 1, serverDeadline)) break;
            }
            Sleep(40);
        } else {
            TimedIo(pipe.get(), true, reply.data(), static_cast<DWORD>(reply.size()), serverDeadline);
            Sleep(40);
        }
        DisconnectNamedPipe(pipe.get());
    });
    std::cout << "Fault case " << static_cast<int>(fault) << std::endl;
    std::vector<ChildData> children;
    const auto start = GetTickCount64();
    const bool ok = Resolve(L"C:\\Test", children);
    const auto elapsed = GetTickCount64() - start;
    // Always join before throwing; scheduling latency is not a protocol deadline.
    server.join();
    Check(elapsed < 150, "callback retained a stalled worker");
    if (fault == Fault::None || fault == Fault::Fragmented)
        Check(ok && children.size() == 2, "valid/fragmented frame rejected");
    else Check(!ok, "malformed/late reply accepted");
}

void NestedMenus() {
    const auto frame = Reply();
    std::vector<std::uint8_t> nodes(frame.begin() + 17, frame.end()); // count + two actions
    for (int level = 0; level < 2; ++level) {
        std::vector<std::uint8_t> group{1, 0, 2, 1, 0, 0};
        group.insert(group.end(), 16, 0);
        AppendUInt32(group, 1); group.push_back('G'); AppendUInt32(group, 0);
        group.insert(group.end(), nodes.begin(), nodes.end());
        nodes = std::move(group);
    }
    nodes.insert(nodes.begin(), 0);
    std::vector<ChildData> children;
    Check(ParseResolveResponse(nodes, children), "two-level submenu rejected");
    Check(children.size() == 1 && children[0].children.size() == 1 &&
        children[0].children[0].children.size() == 2, "submenu hierarchy lost");
    nodes.back() = 1;
    Check(!ParseResolveResponse(nodes, children), "malformed leaf child count accepted");
}


void EnumeratorFailures() {
    CommandEnumerator enumerator({ChildData{}, ChildData{}});
    IExplorerCommand* output[3]{};
    ULONG fetched = 99;
    Check(enumerator.Next(2, nullptr, &fetched) == E_POINTER && fetched == 0, "failure left fetched uninitialized");
    const auto objects = g_objectCount.load();
    g_testCommandAllocationsBeforeFailure = 1;
    Check(enumerator.Next(2, output, &fetched) == E_OUTOFMEMORY, "allocation failure was not injected");
    Check(!output[0] && !output[1] && fetched == 0 && g_objectCount == objects,
        "partial COM enumeration leaked ownership");
    g_testCommandAllocationsBeforeFailure = -1;
    Check(enumerator.Next(2, output, nullptr) == S_OK, "failure advanced cursor or optional fetched rejected");
    output[0]->Release(); output[1]->Release();
    output[0] = reinterpret_cast<IExplorerCommand*>(1);
    Check(enumerator.Next(1, output, &fetched) == S_FALSE && fetched == 0 && !output[0], "end left stale COM pointer");
    enumerator.Reset();
    Check(enumerator.Next(3, output, &fetched) == S_FALSE && fetched == 2 && !output[2], "short enumeration outputs");
    output[0]->Release(); output[1]->Release();
}

// The Shell may release/change its site during an outgoing COM call even in an
// STA. Keep the site alive and reject results from the superseded context.
class ReentrantSite final : public IUnknown {
public:
    ExplorerCommand* command;
    bool& destroyed;
    bool& releasedInsideQuery;
    bool& releaseSawNewSite;
    bool detachOnQuery = false;
    ReentrantSite(ExplorerCommand* owner, bool& gone, bool& inside, bool& detached)
        : command(owner), destroyed(gone), releasedInsideQuery(inside), releaseSawNewSite(detached) {}
    HRESULT QueryInterface(REFIID iid, void** result) noexcept override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (detachOnQuery) {
            // Local copies are deliberate: the old implementation deletes this
            // object in SetSite, before QueryInterface has even returned.
            auto* owner = command;
            auto* gone = &destroyed;
            auto* inside = &releasedInsideQuery;
            owner->SetSite(nullptr);
            *inside = *gone;
            return E_NOINTERFACE;
        }
        if (iid != IID_IUnknown) return E_NOINTERFACE;
        *result = static_cast<IUnknown*>(this); AddRef(); return S_OK;
    }
    ULONG AddRef() noexcept override { return ++references; }
    ULONG Release() noexcept override {
        const auto left = --references;
        if (!left) {
            // Re-enter after the last reference was dropped. GetSite must already
            // see the replacement, not the dying object.
            void* current = nullptr;
            releaseSawNewSite = command->GetSite(IID_IUnknown, &current) == E_FAIL && current == nullptr;
            destroyed = true;
            delete this;
        }
        return left;
    }
private:
    std::atomic_ulong references = 1;
};

void ReentrantContext() {
    ExplorerCommand command(true);
    bool destroyed = false, releasedInside = false, releaseSawNewSite = false;
    auto* site = new ReentrantSite(&command, destroyed, releasedInside, releaseSawNewSite);
    command.SetSite(site);
    site->Release(); // The command now owns the only reference.
    site->detachOnQuery = true;
    EXPCMDSTATE state{};
    Check(command.GetState(nullptr, TRUE, &state) == E_PENDING, "stale reentrant context was published");
    Check(destroyed && !releasedInside && releaseSawNewSite, "site lifetime broken by reentrant callback");
}

void ConcurrentComLifetime() {
    auto* command = new ExplorerCommand(true);
    std::vector<std::thread> callers;
    for (int thread = 0; thread < 8; ++thread) callers.emplace_back([command] {
        for (int iteration = 0; iteration < 10000; ++iteration) {
            command->AddRef();
            EXPCMDSTATE state{};
            command->GetState(nullptr, FALSE, &state);
            command->SetSite(nullptr);
            command->Release();
        }
    });
    for (auto& caller : callers) caller.join();
    Check(command->Release() == 0, "concurrent AddRef/Release lost references");
}

void InvalidOutgoingStrings() {
    std::vector<std::uint8_t> encoded;
    Check(ToUtf8(L"", encoded) && encoded.empty(), "absent directory rejected");
    Check(!ToUtf8(std::wstring(1, static_cast<wchar_t>(0xD800)), encoded), "invalid UTF-16 became an empty directory");
    Check(!ToUtf8(std::wstring(L"C:\\a\0b", 6), encoded), "embedded NUL accepted");
}

int main() {
    try {
        EnumeratorFailures();
        ReentrantContext();
        ConcurrentComLifetime();
        InvalidOutgoingStrings();
        NestedMenus();
        Check(GetCurrentUserSid() != L"unknown", "SID capture");
        // ASan's first CreateThread can take longer than the production deadline.
        // Check that cold requests fail closed, then measure protocol behavior warm.
        // Do not increase the production 30ms deadline to accommodate instrumentation.
        for (int warmup = 0; warmup < 2; ++warmup) {
            std::vector<ChildData> cold;
            const auto start = GetTickCount64();
            Check(!Resolve(L"C:\\Test", cold), "cold missing broker accepted");
            const auto elapsed = GetTickCount64() - start;
            Check(elapsed < 150, "cold callback retained worker");
            for (int i = 0; i < 200 && g_pendingRequests; ++i) Sleep(10);
            Check(g_pendingRequests == 0, "cold worker not reclaimed");
            std::cout << "Cold callback " << elapsed << "ms" << std::endl;
        }
        for (const auto fault : {Fault::None, Fault::Fragmented, Fault::Slow, Fault::HalfHeader,
                Fault::Disconnect, Fault::WrongId, Fault::Oversize, Fault::WrongVersion}) Exchange(fault);
        // Force cleanup to outlive the caller: request memory and module references
        // must remain owned until the worker observes cancellation completion.
        g_testCleanupDelayMs = 300;
        Exchange(Fault::Slow);
        Check(g_pendingRequests > 0 && g_objectCount > 0, "abandoned request lost ownership");
        g_testCleanupDelayMs = 0;
        Sleep(350);
        Check(g_pendingRequests == 0, "delayed cleanup leaked");
        CommandEnumerator enumerator({ChildData{}, ChildData{}});
        Check(enumerator.Skip(2) == S_OK && enumerator.Skip(1) == S_FALSE, "COM Skip at end");
        Sleep(100);
        DWORD before = 0, after = 0;
        GetProcessHandleCount(GetCurrentProcess(), &before);
        for (int i = 0; i < 10000; ++i) {
            std::vector<ChildData> children;
            Check(!Resolve(L"C:\\Test", children), "missing broker accepted");
            Check(g_pendingRequests <= kMaxPendingRequests, "pending quota exceeded");
            if (i % 1000 == 0) std::cout << "Request " << i << std::endl;
        }
        for (int i = 0; i < 200 && g_pendingRequests; ++i) Sleep(10);
        GetProcessHandleCount(GetCurrentProcess(), &after);
        Check(g_pendingRequests == 0, "workers not reclaimed");
        Check(after <= before + 8, "handle count grew after 10000 requests");
        // Capacity exhaustion must fail immediately, without allocating a worker.
        g_pendingRequests = kMaxPendingRequests;
        std::vector<ChildData> children;
        const auto start = GetTickCount64();
        Check(!Resolve(L"C:\\Test", children), "quota did not fail closed");
        Check(GetTickCount64() - start < 50, "quota waited");
        g_pendingRequests = 0;
        std::cout << "Native fault cases and 10000 missing-broker requests passed; handles "
                  << before << " -> " << after << '\n';
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
