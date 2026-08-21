# Pattern: Explorer Failure Boundary

## Goal

ShellCommand 的任何业务错误都不能升级成 Explorer 的稳定性问题。

## Rule 1 — Native Thin Adapter

Explorer DLL 只保留：

```text
COM
path extraction
bounded IPC
DTO mapping
fallback launch
```

禁止业务 parser、插件系统和用户脚本进入此边界。

## Rule 2 — Surrogate

通过 package manifest 的 COM Surrogate 承载 DLL，降低扩展异常直接污染 Explorer 进程的风险。

Surrogate 不是性能豁免：Explorer 仍在等待 COM 返回。

## Rule 3 — No Exception Across ABI

所有 COM interface methods：

```text
validate pointers
try internal work
catch all C++ exceptions
convert to safe HRESULT/state
```

不得让异常穿过 COM ABI。

必要时对不可恢复 native fault 使用 Windows 推荐的进程隔离思路；不要用 catch-all SEH 去假装进程仍可靠。

## Rule 4 — Fail Invisible

菜单构造失败时：

优先：

```text
fallback minimal menu
```

其次：

```text
hide dynamic content
```

禁止：

```text
MessageBox
modal error UI
crash
long retry
```

## Rule 5 — No User Data in Explorer Code

Explorer DLL 不读取：

```text
.shellcommand.yaml
global.shellcommand.yaml
state journal
logs
third-party menu registry catalog
```

唯一用户上下文是当前 filesystem path 和 Broker response。

## Logging

Explorer side 只允许极轻量 diagnostics，例如 ETW/OutputDebugString 或 bounded in-memory counters。

不得在每次右键同步 append/flush 日志文件。

详细日志在 Broker。
