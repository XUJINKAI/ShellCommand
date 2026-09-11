> **v2 重设计契约（feat/redesign-v2）**：本分支按 [已确认设计](../redesign-v2.md) 分阶段重写；下文是重设计前的历史基线，不再对新实现构成兼容性要求。仅接受 YAML v2，不导入或迁移旧配置。实施与验证进度见 [P0](../p0-validation.md)。

# UI Contract

## Scope

ShellCommand.exe 是面向用户的绿色包入口和管理界面，不是命令执行主路径。

V11.0 优先简单清晰，不追求复杂 IDE 式编辑器。

## Main Navigation

两个一级区域：

```text
My Commands
Context Menu Manager
```

附加：Settings/About 可按实现需要提供，但不能抢占主导航。

## My Commands

必须显示：

```text
Shell extension registration status
Broker status
Global config path
Global config validity
Last config error (if any)
```

必须提供：

```text
Open Global Config
Open Config Folder
Validate
Preview for Folder...
Restart Explorer
```

首个用户版本必须提供：

- 自动检测当前绿色包的安装状态；
- `安装 / 修复`；
- `卸载`；
- 安装/卸载后的 Explorer 重启提示；
- 安装失败时显示具体阶段和错误信息。

开发/诊断场景可额外提供：

```text
Register / Repair Integration
Unregister Integration
```

绿色包入口本身负责安装和卸载，不要求用户运行 PowerShell 或其他安装脚本。

### Diagnostics

配置错误显示：

- 文件；
- 行；
- 列；
- code；
- message。

配置错误不能用仅有 `Something went wrong` 的模糊提示替代。

## Preview

用户选择一个目录后，App 通过 Broker `ResolveMenu` 同语义路径预览最终菜单。

Preview 不允许自己重新实现一份 Match/YAML 逻辑。

必须能区分：

```text
Directory commands
Global commands
Built-in actions
```

视觉上是否分组可自由实现。

## Context Menu Manager

列表至少显示：

```text
Display Name
Type
Scope
State
Source/Application
```

Details 至少显示：

```text
registration source/path
CLSID (if any)
package identity (if any)
command / DLL path when discoverable
current block/visibility state
whether ShellCommand can safely modify it
```

### Type

```text
Static Verb
Legacy COM
Packaged Explorer Command
System / Unknown
```

### Scope filter

至少：

```text
Files
Directory
Directory Background
Drive
All / Other
```

### State

```text
Enabled
Blocked
Hidden by verb
Read-only
Unknown
Pending Explorer Restart
```

## Enable / Disable Interaction

如果 entry 支持可逆修改：

- 提供 toggle/action；
- 修改前记录 exact prior state；
- 操作完成后标记是否需要 Restart Explorer；
- restore 必须恢复原值，不是简单“删除所有相关值”。

如果不支持安全修改：

- 不显示假的 toggle；
- 显示 `Read-only`；
- Details 解释来源。

## No Ordering UI

V11 不提供 drag-to-reorder Windows 11 modern context menu。

不要做一个能拖但实际上无法可靠生效的假功能。

## Elevation

普通浏览/扫描不要求管理员。

需要 machine-level registry 写入时：

- 用户明确点击操作后才请求 UAC；
- Broker 不永久提升；
- UI 必须在操作前显示目标 entry，而不是“以管理员重新启动整个 App 后随便写”。
