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
    }
    return BuildFrame(kResolveResponse, 1, payload);
}

enum class Fault { None, Slow, HalfHeader, Fragmented, Disconnect, WrongId, Oversize, WrongVersion };
void Exchange(Fault fault) {
    const auto name = L"\\\\.\\pipe\\ShellCommand11.Test." + std::to_wstring(GetCurrentProcessId());
    ScopedHandle pipe(CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr));
    Check(pipe.get() != INVALID_HANDLE_VALUE, "create fake broker");
    std::thread server([&] {
        if (!ConnectNamedPipe(pipe.get(), nullptr) && GetLastError() != ERROR_PIPE_CONNECTED) return;
        std::uint8_t input[4096]{};
        DWORD count = 0;
        if (!ReadFile(pipe.get(), input, sizeof(input), &count, nullptr)) return;
        auto reply = Reply();
        if (fault == Fault::Disconnect) { DisconnectNamedPipe(pipe.get()); return; }
        if (fault == Fault::Slow) Sleep(100);
        if (fault == Fault::WrongId) reply[8] = 99;
        if (fault == Fault::WrongVersion) reply[4] = 1;
        if (fault == Fault::Oversize) reply[15] = 127;
        if (fault == Fault::HalfHeader) {
            WriteFile(pipe.get(), reply.data(), 5, &count, nullptr);
            Sleep(100);
        } else if (fault == Fault::Fragmented) {
            for (const auto byte : reply) {
                if (!WriteFile(pipe.get(), &byte, 1, &count, nullptr)) break;
            }
            Sleep(40);
        } else {
            WriteFile(pipe.get(), reply.data(), static_cast<DWORD>(reply.size()), &count, nullptr);
            Sleep(40);
        }
        DisconnectNamedPipe(pipe.get());
    });
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

int main() {
    try {
        Check(GetCurrentUserSid() != L"unknown", "SID capture");
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
