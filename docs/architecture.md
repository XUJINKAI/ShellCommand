> **v2 重设计契约（feat/redesign-v2）**：本分支按 [已确认设计](redesign-v2.md) 分阶段重写；下文是重设计前的历史基线，不再对新实现构成兼容性要求。仅接受 YAML v2，不导入或迁移旧配置。实施与验证进度见 [P0](p0-validation.md)。

# ShellCommand 11 系统架构

## 目标

架构首先解决两个问题：

1. Windows 11 Explorer 扩展必须足够薄、足够快、足够难崩。
2. YAML、规则、缓存、执行和菜单管理等复杂逻辑必须脱离 Explorer 进程边界。

## 组件

```text
ShellCommand11/
├── src/
│   ├── ShellCommand.Core/
│   ├── ShellCommand.Config.Yaml/
│   ├── ShellCommand.Broker/
│   ├── ShellCommand.App/
│   └── ShellCommand.Explorer/
├── packaging/
└── tests/
```

### ShellCommand.Core

纯业务模型与规则。

负责：

- Command / Menu / Match 等语言无关模型；
- Match 表达式解析与评价语义；
- Global + Directory 配置合并；
- 从配置和目录事实得到 Resolved Menu；
- 与 UI、进程、COM、YAML 无关的验证规则。

不得依赖：

```text
WPF
Win32 Registry
COM
MSIX
Named Pipe
YamlDotNet
ShellExecute
Explorer
```

### ShellCommand.Config.Yaml

YAML Adapter。

负责：

- YAML 文本解析；
- YAML 子集限制；
- 字段映射；
- schema/semantic validation；
- 把输入转换成 Core Model；
- 产生结构化诊断。

它依赖 Core；Core 不依赖它。

### ShellCommand.Broker

每用户一个、登录后保持运行的后台进程。

Broker 是复杂工作的宿主，而不是 Windows Service。

负责：

- Named Pipe Server；
- 配置发现和缓存；
- YAML Adapter 调用；
- Last Known Good；
- 文件存在性/Match 所需的 Windows 文件系统探测；
- 菜单解析；
- 动作 token 生命周期；
- 用户命令执行；
- Context Menu Manager 的扫描与修改；
- 诊断与日志。

Broker 必须事件驱动，不允许周期性全盘扫描或固定间隔轮询。

### ShellCommand.Explorer

Native C++ COM DLL。

负责：

- 实现 `IExplorerCommand`；
- 从 `IShellItemArray` / Shell context 获取工作目录；
- 通过 Named Pipe 请求 Broker；
- 把 IPC `MenuItem` 映射为 Explorer 子命令；
- 把用户点击转换为 `Invoke` IPC；
- Broker 不可用时提供最小 fallback。

不得：

- 引入 CLR；
- 解析 YAML；
- 扫描目录；
- 运行用户命令；
- 查询网络；
- 初始化重量级日志框架；
- 直接管理第三方注册表菜单；
- 在 COM UI 回调中无限等待。

### ShellCommand.App

.NET 10 WPF 用户界面。

负责：

- 显示状态与诊断；
- 打开/编辑/验证配置；
- 预览菜单；
- 显示和管理已安装右键菜单；
- 安装/注册状态操作；
- 请求重启 Explorer。

App 通过 Broker 获取业务状态。它不复制一份 YAML 解析/扫描逻辑。

### Packaging

负责：

- 给 Win32 应用提供 package identity；
- 注册 `windows.comServer`；
- 用 COM Surrogate 承载 Explorer DLL；
- 注册 `windows.fileExplorerContextMenus`；
- `Directory\Background` ItemType；
- 安装/升级/卸载时保持注册一致性。

采用 sparse package / packaging with external location，不要求把主程序完全迁入 MSIX。

## 进程图

```text
┌──────────────────────────┐
│       explorer.exe       │
└────────────┬─────────────┘
             │ COM activation
             ▼
┌──────────────────────────┐
│      COM Surrogate       │
│                          │
│ ShellCommand.Explorer    │
│ native DLL               │
└────────────┬─────────────┘
             │ local Named Pipe
             ▼
┌──────────────────────────┐
│ ShellCommand.Broker.exe  │
│ .NET 10                  │
│                          │
│ Core + YAML + Cache      │
│ Execute + Menu Manager   │
└────────────┬─────────────┘
             ▲
             │ local Named Pipe
┌────────────┴─────────────┐
│ ShellCommand.exe         │
│ .NET 10 WPF              │
└──────────────────────────┘
```

## 依赖规则

允许：

```text
Config.Yaml  → Core
Broker       → Core
Broker       → Config.Yaml
App          → Broker protocol/client
Explorer     → Broker IPC contract (wire format only)
Packaging   → Explorer/App/Broker binaries
```

禁止：

```text
Core → Config.Yaml
Core → Broker
Core → Windows UI
Explorer → Core managed assembly
Explorer → Config.Yaml
Explorer → App
App → Explorer implementation
```

## Explorer 快路径

Explorer 的 UI 路径只允许：

```text
Get context path
        ↓
Open existing local pipe
        ↓
Send bounded request
        ↓
Read bounded response
        ↓
Create COM menu objects
```

所有输入、输出都有大小限制和硬超时。

`GetState` 不为动态菜单发 IPC。ShellCommand 根入口保持稳定可用；真正的动态列表在 `EnumSubCommands` 进行一次 Resolve 请求。

## Broker 生命周期

V11.0 的明确选择：

- 每用户会话一个 Broker；
- 用户登录后启动；
- 会话期间保持运行；
- 无窗口；
- 无定时轮询；
- 没有 Explorer 请求时阻塞等待 pipe/event；
- Broker 崩溃不能导致 Explorer 崩溃；
- Broker 不可用时 Explorer 不在菜单构建路径中同步冷启动 .NET Runtime。

以后若实测内存成本不合理，可重新评估 idle-exit，但 V11.0 先换取稳定的 warm latency。

## 配置缓存边界

缓存的是：

```text
Parsed + Validated Config
```

而不是永久缓存最终菜单。

每次 Resolve 仍基于当前目录 facts 执行 Match。

配置发生变化时使用 Last Known Good 原子替换；解析失败不替换旧配置。

## 安全/权限边界

Broker 默认以当前用户普通权限运行。

用户命令 `RunAsAdmin: true` 时，提权只发生在该命令启动阶段。

右键菜单管理器优先使用 HKCU 可逆控制。需要 HKLM 写入时必须显式触发 UAC；不得让 Broker 永久以管理员身份常驻。

## 可测试边界

必须可以在没有 Explorer 的情况下测试：

- Core；
- YAML parser；
- config cache；
- Match；
- Broker IPC；
- process launch plan；
- registry/menu scanner。

Explorer DLL 的测试重点是：

- COM contract；
- malformed IPC response；
- timeout；
- Broker crash；
- zero/large menu；
- Unicode path；
- Explorer restart / registration。
