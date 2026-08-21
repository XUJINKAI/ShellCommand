# Decision 0001: Native IExplorerCommand for Windows 11

## Decision

ShellCommand 11 使用 native C++ COM DLL 实现 `IExplorerCommand`，通过 Windows 11 modern context menu 注册。

不继续使用 SharpShell / managed `IContextMenu` 作为 V11 主路径。

## Reason

- Windows 11 modern File Explorer context menu 的正式扩展模型是 `IExplorerCommand` + app identity；
- native DLL 可以避免在 Shell 扩展路径中加载 CLR；
- 可以把扩展控制得非常薄；
- 与微软当前文档和实际 VS Code Explorer extension 路线一致。

## Rejected

### Keep SharpShell

可以继续出现在 legacy `Show more options`，但不能满足 V11 的产品目标。

### Managed COM inside Explorer

即使技术上可实现，也会扩大 Explorer 内 CLR/managed dependency 边界，不符合稳定性目标。

## Reconsider When

Microsoft 提供新的、明确推荐的 out-of-process modern context menu API，且能覆盖动态子命令。
