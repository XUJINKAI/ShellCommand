# Workflow: Build Menu

## Trigger

用户在 Windows 11 文件夹背景执行右键，Explorer 构造 ShellCommand 子菜单。

## Participants

```text
Explorer
ShellCommand.Explorer
ShellCommand.Broker
Config Runtime
Custom Menu
```

## Flow

```text
Explorer asks for subcommands
        │
        ▼
Explorer DLL resolves working directory
        │
        ├── no filesystem path → fallback only
        │
        ▼
ResolveMenuRequest(path)
        │
        ├── pipe unavailable/timeout → fallback only
        │
        ▼
Broker gets Global Config LKG/cache
        │
        ▼
Broker probes local .shellcommand.yaml metadata
        │
        ├── cached unchanged → reuse parsed config
        ├── changed with LKG → use LKG now, queue refresh
        └── first-seen → bounded parse or defer local config
        │
        ▼
Custom Menu evaluates Match lazily
        │
        ▼
Normalize + issue ActionTokens
        │
        ▼
ResolveMenuResponse
        │
        ▼
Explorer maps DTO to IExplorerCommand children
        │
        ▼
Menu shown
```

## Timing Semantics

Explorer hard deadline dominates correctness of “latest file contents”。

如果 local config 刚刚变化但无法在预算内完成刷新，允许本次菜单使用 Last Known Good；不允许让 Explorer 等待。

## Fallback

任何 Broker/IPC 失败：

```text
ShellCommand >
    Open ShellCommand 11
```

不弹错误。

## No Commands

即使没有用户 command，正常 Broker 响应仍至少包含 `Open ShellCommand 11`，以及按配置启用的 built-in actions。

## Verification

测试至少覆盖：

- no local config；
- valid local + global；
- Match changes after file creation/deletion；
- invalid local with LKG；
- invalid local without LKG；
- Broker unavailable；
- slow Broker；
- Unicode path；
- 100 items boundary。
