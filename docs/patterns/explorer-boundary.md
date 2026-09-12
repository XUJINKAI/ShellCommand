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

## COM lifetime and reentrancy

`SetSite` 先发布新 site 并清空上下文，再释放旧 site。`GetSite` / 上下文提取在调用外部 COM 前持有独立引用；不能在外部 QueryInterface、Release、Shell API 或 IPC 期间持有状态锁。STA 不排除外呼期间的重入。选择查询用 generation 发布快照；新 site 或后续 GetState 使旧结果失效，快查询不能沿用旧目录。EnumSubCommands 使用自己的局部菜单容器。

对象引用计数使用原子操作。枚举游标受短锁保护；Next 初始化所有输出，允许省略 fetched，分配失败释放全部临时接口且保持游标不变。动态分组、分隔和 fallback 没有注册的 canonical verb，返回 GUID_NULL，不复用根 CLSID。

Windows CI 除源文件级 IPC/ASan 测试外，必须加载实际发布 DLL，经过导出的工厂创建命令，验证 COM 枚举并循环卸载；安装测试在真实注册后用 CLSCTX_LOCAL_SERVER 激活 Surrogate，验证跨进程接口调用。它们仍不等于 Explorer UI 实机验收。

接口依据：[GetState](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iexplorercommand-getstate)、[Next 的可选 fetched](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ienumexplorercommand-next)。
