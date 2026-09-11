> **v2 重设计契约（feat/redesign-v2）**：本分支按 [已确认设计](../redesign-v2.md) 分阶段重写；下文是重设计前的历史基线，不再对新实现构成兼容性要求。仅接受 YAML v2，不导入或迁移旧配置。实施与验证进度见 [P0](../p0-validation.md)。

# Explorer Contract

## Registration

ShellCommand 11 自己的动态菜单必须注册到 Windows 11 modern File Explorer context menu：

```text
ItemType = Directory\Background
```

必须使用：

- native COM DLL；
- `IExplorerCommand`；
- package identity；
- `windows.comServer`；
- COM Surrogate；
- `windows.fileExplorerContextMenus`。

不得把 V11 自定义命令只放进 `Show more options` 作为完成标准。

## Root Menu

一级入口标题：

```text
ShellCommand
```

或资源本地化后的等价显示名。

根菜单通过 `EnumSubCommands` 提供一层子命令。

V11 不支持动态子命令继续拥有下一级动态子菜单。

## Required IExplorerCommand Behavior

### GetTitle

- 根命令返回 ShellCommand；
- child 返回 Broker 提供的 title；
- 不访问 Broker 之外的任何外部资源；
- 不解析 config。

### GetIcon

- 根命令使用随产品安装的稳定图标；
- child 使用 Broker 已解析的 `iconRef`；
- icon 无效时返回无图标，不让菜单失败。

### GetState

根命令：

- 正常安装状态下保持 enabled；
- 不为判断动态子项而同步请求 Broker；
- 不扫描目录。

### GetFlags

根命令声明有 subcommands。

### EnumSubCommands

- 获取当前工作目录；
- 发出一次 `ResolveMenuRequest`；
- 在硬 deadline 内返回；
- Broker 正常时映射完整 child list；
- Broker 超时/异常时返回最小 fallback child：`Open ShellCommand 11`。

### Invoke

动态 action：

- 发送 token 给 Broker；
- Broker 接受后立即返回；
- 不等待实际命令完成。

fallback `Open ShellCommand 11`：

- 允许 Explorer DLL 直接启动固定、受信任的 ShellCommand App 路径；
- 这是唯一允许不依赖 Broker 的启动动作；
- 路径来自安装布局/manifest，不来自用户配置。

## Context Path

`Directory\Background` 的 working directory 必须是用户右键所在的实际文件系统目录。

如果 Shell item 无法转换为 filesystem path：

- 不发送 Resolve；
- 只提供 fallback 或隐藏动态内容；
- 不猜测 virtual shell namespace 到 filesystem 的映射。

## Failure Contract

任何以下故障不得传播到 Explorer：

```text
Broker not running
Broker crash
pipe permission error
IPC timeout
bad response
bad UTF-8
invalid config
icon error
user command failure
logging failure
```

Explorer 侧禁止 MessageBox、modal UI、同步错误对话框。

## Performance Contract

菜单 UI path 不允许：

- 网络；
- YAML；
- recursive IO；
- 进程启动（fallback Open App 的 Invoke 除外）；
- package enumeration；
- registry scanning；
- context menu manager scanning；
- log file flush；
- sleep/retry loops。

硬时间预算见 `patterns/ipc-and-time-budget.md`。

## Threading / ABI

- COM 类按 manifest 声明的 STA model 实现；
- 所有 exported COM entry 和 interface method 必须防止 C++ exception 越界；
- invalid pointer/input 使用合适 HRESULT；
- ordinary runtime failure 优先返回安全菜单状态，而不是 crash。

## Explorer Restart

安装、升级、卸载或菜单管理修改后可能需要 Explorer 重新加载注册。

ShellCommand App 可提供显式 `Restart Explorer` 操作，但不得在无用户动作时随意杀掉 Explorer。
