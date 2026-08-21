#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <sddl.h>
#include <shellapi.h>
#include <vector>
#include <string>
#include <atomic>
#include <algorithm>
#include <new>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
constexpr unsigned short kVersion = 1;
constexpr unsigned long kMaxPayload = 256 * 1024;
constexpr unsigned long kDeadlineMs = 30;
constexpr GUID kClsid = { 0xB8B3D4E2, 0x11A0, 0x4A5B, { 0x8D, 0x2C, 0x7D, 0x0A, 0x0D, 0xB1, 0x01, 0x11 } };
std::atomic_ulong g_objects = 0;
std::atomic_ulong g_locks = 0;

void Put16(std::vector<unsigned char>& b, unsigned short v) { b.push_back((unsigned char)v); b.push_back((unsigned char)(v >> 8)); }
void Put32(std::vector<unsigned char>& b, unsigned long v) { for (int i = 0; i < 4; ++i) b.push_back((unsigned char)(v >> (8 * i))); }
unsigned short Get16(const unsigned char* p) { return (unsigned short)(p[0] | (p[1] << 8)); }
unsigned long Get32(const unsigned char* p) { return (unsigned long)p[0] | ((unsigned long)p[1] << 8) | ((unsigned long)p[2] << 16) | ((unsigned long)p[3] << 24); }

bool TimedIo(HANDLE pipe, bool write, void* data, DWORD length, ULONGLONG deadline) noexcept {
    OVERLAPPED ov{}; ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!ov.hEvent) return false;
    DWORD done = 0; BOOL ok = write ? WriteFile(pipe, data, length, &done, &ov) : ReadFile(pipe, data, length, &done, &ov);
    if (!ok && GetLastError() != ERROR_IO_PENDING) { CloseHandle(ov.hEvent); return false; }
    if (!ok) {
        const auto now = GetTickCount64();
        const DWORD wait = now >= deadline ? 0 : (DWORD)std::min<ULONGLONG>(deadline - now, 0xFFFFFFFFull);
        if (WaitForSingleObject(ov.hEvent, wait) != WAIT_OBJECT_0) { CancelIoEx(pipe, &ov); CloseHandle(ov.hEvent); return false; }
        if (!GetOverlappedResult(pipe, &ov, &done, FALSE)) { CloseHandle(ov.hEvent); return false; }
    }
    CloseHandle(ov.hEvent);
    return done == length;
}

std::wstring UserSid() noexcept {
    HANDLE token = nullptr; if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return L"unknown";
    DWORD size = 0; GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<unsigned char> buffer(size); std::wstring result = L"unknown";
    if (GetTokenInformation(token, TokenUser, buffer.data(), size, &size)) {
        LPWSTR sid = nullptr; if (ConvertSidToStringSidW(((TOKEN_USER*)buffer.data())->User.Sid, &sid)) { result = sid; LocalFree(sid); }
    }
    CloseHandle(token); return result;
}

bool Utf8(const std::wstring& value, std::vector<unsigned char>& out) noexcept {
    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), (int)value.size(), nullptr, 0, nullptr, nullptr);
    if (size < 0 || size > 32 * 1024) return false; out.resize(size);
    return size == 0 || WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), (int)value.size(), (char*)out.data(), size, nullptr, nullptr) == size;
}
bool ReadString(const std::vector<unsigned char>& p, size_t& pos, std::wstring& result) noexcept {
    if (pos + 4 > p.size()) return false; const auto length = Get32(p.data() + pos); pos += 4;
    if (length > 32 * 1024 || pos + length > p.size()) return false;
    const int chars = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char*)p.data() + pos, (int)length, nullptr, 0);
    if (chars < 0) return false; result.resize(chars);
    if (chars && MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char*)p.data() + pos, (int)length, result.data(), chars) != chars) return false;
    pos += length; return true;
}

struct ChildData { std::wstring title; std::wstring icon; unsigned char token[16]{}; bool fallback = false; bool separator = false; };

bool Resolve(const std::wstring& directory, std::vector<ChildData>& children) noexcept {
    const std::wstring name = L"\\\\.\\pipe\\ShellCommand11." + UserSid();
    HANDLE pipe = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    std::vector<unsigned char> path; if (!Utf8(directory, path)) { CloseHandle(pipe); return false; }
    std::vector<unsigned char> payload; Put32(payload, (unsigned long)path.size()); payload.insert(payload.end(), path.begin(), path.end());
    std::vector<unsigned char> frame = { 'S','C','1','1' }; Put16(frame, kVersion); Put16(frame, 10); Put32(frame, 1); Put32(frame, (unsigned long)payload.size()); frame.insert(frame.end(), payload.begin(), payload.end());
    const auto deadline = GetTickCount64() + kDeadlineMs;
    bool ok = TimedIo(pipe, true, frame.data(), (DWORD)frame.size(), deadline);
    unsigned char header[16]{}; if (ok) ok = TimedIo(pipe, false, header, sizeof(header), deadline);
    std::vector<unsigned char> response; if (ok && header[0]=='S' && header[1]=='C' && header[2]=='1' && header[3]=='1' && Get16(header+4)==kVersion && Get16(header+6)==11 && Get32(header+12)<=kMaxPayload) { response.resize(Get32(header+12)); ok = response.empty() || TimedIo(pipe, false, response.data(), (DWORD)response.size(), deadline); }
    CloseHandle(pipe); if (!ok || response.size() < 3) return false;
    const auto count = Get16(response.data() + 1); if (count > 100) return false; size_t pos = 3;
    for (unsigned short i = 0; i < count; ++i) {
        if (pos + 20 > response.size()) return false; ChildData child{}; const auto kind = response[pos++]; const auto flags = response[pos++];
        if (flags & 0xFE || response[pos] || response[pos+1]) return false; pos += 2; memcpy(child.token, response.data()+pos, 16); pos += 16;
        if (!ReadString(response, pos, child.title) || !ReadString(response, pos, child.icon)) return false;
        if (kind > 1 || (kind == 1 && (!child.title.empty() || !child.icon.empty()))) return false; child.separator = kind == 1; child.fallback = false; children.push_back(std::move(child));
    }
    return pos == response.size();
}

bool InvokeToken(const unsigned char token[16]) noexcept {
    const std::wstring name = L"\\\\.\\pipe\\ShellCommand11." + UserSid();
    HANDLE pipe = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    std::vector<unsigned char> frame = { 'S','C','1','1' }; Put16(frame, kVersion); Put16(frame, 20); Put32(frame, 2); Put32(frame, 16); frame.insert(frame.end(), token, token + 16);
    const auto deadline = GetTickCount64() + kDeadlineMs; bool ok = TimedIo(pipe, true, frame.data(), (DWORD)frame.size(), deadline); unsigned char header[16]{};
    if (ok) ok = TimedIo(pipe, false, header, sizeof(header), deadline);
    unsigned char status = 255; if (ok && header[0]=='S'&&header[1]=='C'&&header[2]=='1'&&header[3]=='1'&&Get16(header+4)==kVersion&&Get16(header+6)==21&&Get32(header+12)==1) ok=TimedIo(pipe,false,&status,1,deadline);
    CloseHandle(pipe); return ok && status == 0;
}

bool ExtractPath(IShellItemArray* items, std::wstring& path) noexcept {
    if (!items) return false; IShellItem* item = nullptr; if (FAILED(items->GetItemAt(0, &item))) return false;
    PWSTR value = nullptr; const HRESULT hr = item->GetDisplayName(SIGDN_FILESYSPATH, &value); item->Release();
    if (FAILED(hr) || !value) return false; path.assign(value); CoTaskMemFree(value); return !path.empty();
}

class CommandEnumerator;
class ExplorerCommand final : public IExplorerCommand {
    ULONG refs_ = 1; bool root_; ChildData data_{}; std::vector<ChildData> children_; std::wstring currentDirectory_;
public:
    explicit ExplorerCommand(bool root) noexcept : root_(root) { ++g_objects; }
    explicit ExplorerCommand(ChildData data) noexcept : root_(false), data_(std::move(data)) { ++g_objects; }
    ~ExplorerCommand() noexcept override { --g_objects; }
    HRESULT QueryInterface(REFIID iid, void** result) noexcept override { if (!result) return E_POINTER; *result=nullptr; if (iid==IID_IUnknown || iid==IID_IExplorerCommand) { *result=static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; } return E_NOINTERFACE; }
    ULONG AddRef() noexcept override { return ++refs_; }
    ULONG Release() noexcept override { const auto value=--refs_; if (!value) delete this; return value; }
    HRESULT GetTitle(IShellItemArray*, LPWSTR* title) noexcept override { if (!title) return E_POINTER; const wchar_t* value=root_?L"ShellCommand":data_.title.c_str(); size_t bytes=(wcslen(value)+1)*sizeof(wchar_t); *title=(LPWSTR)CoTaskMemAlloc(bytes); if(!*title) return E_OUTOFMEMORY; memcpy(*title,value,bytes); return S_OK; }
    HRESULT GetIcon(IShellItemArray*, LPWSTR* icon) noexcept override { if (!icon) return E_POINTER; const wchar_t* value=root_?L"%SystemRoot%\\System32\\shell32.dll,-167":data_.icon.c_str(); if(!*value) return S_FALSE; size_t bytes=(wcslen(value)+1)*sizeof(wchar_t); *icon=(LPWSTR)CoTaskMemAlloc(bytes); if(!*icon) return E_OUTOFMEMORY; memcpy(*icon,value,bytes); return S_OK; }
    HRESULT GetToolTip(IShellItemArray*, LPWSTR* value) noexcept override { if (!value) return E_POINTER; *value=nullptr; return E_NOTIMPL; }
    HRESULT GetCanonicalName(GUID* value) noexcept override { if(!value) return E_POINTER; *value=kClsid; return S_OK; }
    HRESULT GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) noexcept override { if(!state) return E_POINTER; if(root_ && !ExtractPath(items, currentDirectory_)) currentDirectory_.clear(); *state=ECS_ENABLED; return S_OK; }
    HRESULT GetFlags(EXPCMDFLAGS* flags) noexcept override { if(!flags) return E_POINTER; *flags=root_?ECF_HASSUBCOMMANDS:(data_.separator?ECF_ISSEPARATOR:ECF_DEFAULT); return S_OK; }
    HRESULT Invoke(IShellItemArray*, IBindCtx*) noexcept override;
    HRESULT EnumSubCommands(IEnumExplorerCommand** result) noexcept override;
};

class CommandEnumerator final : public IEnumExplorerCommand {
    ULONG refs_=1; std::vector<ChildData> items_; ULONG index_=0;
public:
    explicit CommandEnumerator(std::vector<ChildData> items) noexcept:items_(std::move(items)){++g_objects;}
    ~CommandEnumerator() noexcept override{--g_objects;}
    HRESULT QueryInterface(REFIID iid,void**r)noexcept override{if(!r)return E_POINTER;*r=nullptr;if(iid==IID_IUnknown||iid==IID_IEnumExplorerCommand){*r=static_cast<IEnumExplorerCommand*>(this);AddRef();return S_OK;}return E_NOINTERFACE;}
    ULONG AddRef()noexcept override{return ++refs_;} ULONG Release()noexcept override{auto n=--refs_;if(!n)delete this;return n;}
    HRESULT Next(ULONG c,IExplorerCommand** out,ULONG* fetched)noexcept override{if(!out)return E_POINTER;ULONG n=0;while(n<c&&index_<items_.size()){out[n]=new(std::nothrow)ExplorerCommand(items_[index_++]);if(!out[n])return E_OUTOFMEMORY;++n;}if(fetched)*fetched=n;return n==c?S_OK:S_FALSE;}
    HRESULT Skip(ULONG c)noexcept override{index_=(ULONG)std::min<size_t>(items_.size(),index_+c);return index_<items_.size()?S_OK:S_FALSE;}
    HRESULT Reset()noexcept override{index_=0;return S_OK;}
    HRESULT Clone(IEnumExplorerCommand** r)noexcept override{if(!r)return E_POINTER;auto* e=new(std::nothrow)CommandEnumerator(items_);if(!e)return E_OUTOFMEMORY;e->index_=index_;*r=e;return S_OK;}
};

HRESULT ExplorerCommand::EnumSubCommands(IEnumExplorerCommand** result) noexcept { if(!result)return E_POINTER;*result=nullptr; if(!root_)return E_NOTIMPL; children_.clear(); if(!currentDirectory_.empty()) Resolve(currentDirectory_, children_); if(children_.empty()){ChildData fallback{};fallback.title=L"Open ShellCommand 11";fallback.fallback=true;children_.push_back(fallback);} auto* e=new(std::nothrow)CommandEnumerator(children_);if(!e)return E_OUTOFMEMORY;*result=e;return S_OK; }
HRESULT ExplorerCommand::Invoke(IShellItemArray*, IBindCtx*) noexcept { if(data_.separator)return E_FAIL; if(root_||data_.fallback){wchar_t module[MAX_PATH]{};GetModuleFileNameW((HMODULE)&__ImageBase,module,MAX_PATH);PathRemoveFileSpecW(module);PathAppendW(module,L"ShellCommand.App.exe");ShellExecuteW(nullptr,L"open",module,nullptr,nullptr,SW_SHOWNORMAL);return S_OK;}return InvokeToken(data_.token)?S_OK:E_FAIL; }

class Factory final : public IClassFactory { ULONG refs_=1; public: HRESULT QueryInterface(REFIID i,void**r)noexcept override{if(!r)return E_POINTER;*r=nullptr;if(i==IID_IUnknown||i==IID_IClassFactory){*r=static_cast<IClassFactory*>(this);AddRef();return S_OK;}return E_NOINTERFACE;} ULONG AddRef()noexcept override{return ++refs_;} ULONG Release()noexcept override{auto n=--refs_;if(!n)delete this;return n;} HRESULT CreateInstance(IUnknown* outer,REFIID iid,void**r)noexcept override{if(outer)return CLASS_E_NOAGGREGATION;auto*c=new(std::nothrow)ExplorerCommand(true);if(!c)return E_OUTOFMEMORY;auto hr=c->QueryInterface(iid,r);c->Release();return hr;} HRESULT LockServer(BOOL lock)noexcept override{if(lock)++g_locks;else--g_locks;return S_OK;}};
}

extern "C" HRESULT __declspec(dllexport) DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) noexcept { if(clsid!=kClsid)return CLASS_E_CLASSNOTAVAILABLE;auto*f=new(std::nothrow)Factory();if(!f)return E_OUTOFMEMORY;auto hr=f->QueryInterface(iid,result);f->Release();return hr; }
extern "C" HRESULT __declspec(dllexport) DllCanUnloadNow() noexcept { return g_objects==0&&g_locks==0?S_OK:S_FALSE; }
