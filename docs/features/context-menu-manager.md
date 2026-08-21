# Feature: Context Menu Manager

## Responsibility

发现 Windows Explorer 右键菜单来源，解释它们的类型与注册位置，并在存在明确可逆机制时禁用/恢复。

## Product Principle

```text
Discovery != Mutability
```

能看到一个菜单项，不代表 ShellCommand 有资格安全修改它。

## Discovery Types

### Static Verb

典型来源：

```text
HKCR\*\shell
HKCR\Directory\shell
HKCR\Directory\Background\shell
HKCR\Drive\shell
HKCR\AllFilesystemObjects\shell
...
```

必须尽量解析 HKCR merged view 对应的真实用户/机器来源，避免恢复时写错 hive。

### Legacy COM Context Menu Handler

典型来源：

```text
...\shellex\ContextMenuHandlers
```

记录：

- handler name；
- CLSID；
- InprocServer path；
- scope；
- source hive/key；
- publisher/file metadata if cheap and available。

### Packaged Explorer Command

现代 Windows 11 app identity 注册的 `IExplorerCommand`。

发现应基于 package registration / manifest 信息，必要时关联 Packaged COM class，记录：

- package identity；
- application；
- ItemType；
- Verb Id；
- CLSID；
- COM DLL；
- display metadata。

不得修改：

```text
HKCR\PackagedCom
AppRepository internals
installed AppxManifest.xml
package files
```

### System / Unknown

无法安全归类时保留为只读信息，不猜测删除办法。

## Supported Scopes

至少扫描：

```text
Files (*)
Directory
Directory Background
Drive
AllFilesystemObjects / Folder when relevant
```

## Disable Strategies

### Static Verb

优先使用可逆 visibility marker，例如 Windows 文档支持的 `ProgrammaticAccessOnly`，而不是删除原 key。

修改前记录：

- exact source key；
- value 是否原本存在；
- 原 type/value。

如果该 verb 原本就含这些 visibility 标记，ShellCommand 不能声称“是自己禁用的”。

### COM Handler

可使用当前用户范围：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked
```

以 CLSID 为 value name 的阻止机制。

修改前记录该 value 的原状态。

### Packaged IExplorerCommand

若有明确 CLSID，可尝试同一 per-user `Shell Extensions\Blocked` 机制；这是 **best-effort capability**，不是修改 package registration。

UI 必须标明：

```text
Blocked via CLSID; Explorer restart may be required
```

如果不能获得稳定 CLSID 或系统拒绝该机制，则 entry 为 read-only。

## State Journal

ShellCommand 进行的每次修改都写入自己的 journal：

```text
operation id
timestamp
entry identity
target registry path/value
previous existence/type/data
new state
```

恢复使用 journal 中 exact previous state。

不要用“恢复默认值”的猜测代替恢复。

## Built-in / Protected

对明显属于 Windows 核心 Shell 行为的项目默认只读。

除非存在公开、稳定且与该 feature 契约一致的机制，否则不提供 toggle。

## Explorer Reload

菜单状态变化可能需要 restart Explorer 或重新登录。

管理器操作后：

- 标记 `Pending Explorer Restart`；
- 提供用户主动的 Restart Explorer；
- 不自动杀 Explorer。

## No Reorder

不扫描、不写入任何所谓“排序值”来声称能任意控制 Windows 11 modern menu 顺序。

## Invariants

- 不删除第三方 registration 作为 disable；
- 不改 AppX database；
- 不写 package 安装目录；
- 所有修改必须可逆；
- 只读条目永远不显示可点击 toggle；
- scanning 本身无系统副作用。
