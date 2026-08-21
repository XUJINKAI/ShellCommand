# Feature: Command Execution

## Responsibility

将用户点击的 immutable action plan 以正确 working directory 和权限启动。

## Owns

- variable expansion；
- command line validation；
- normal launch；
- UAC launch；
- working directory；
- action token consume；
- launch diagnostics。

## Does Not Own

- menu rendering；
- YAML parsing；
- Explorer COM；
- terminal emulator behavior；
- command process lifetime management beyond successful launch（V11）。

## Action Plan

菜单 Resolve 时 Broker 生成 immutable action plan：

```text
workingDirectory
raw command
expanded command
runAsAdmin
resolved metadata
```

Explorer 只持有 opaque token。

## Execution Semantics

### Normal

以当前用户权限启动，working directory 设置为右键目录。

### RunAsAdmin

通过 Windows UAC `runas` 语义启动目标命令。

Broker 本身不提升并保持管理员状态。

## Asynchronous Contract

Explorer `Invoke` 的成功定义只是：

```text
Broker accepted action
```

Broker 接管后立即脱离 Explorer 调用链。

不要让 Explorer 等待：

```text
git pull
build
script
terminal process
```

完成。

## Command Line

V11 延续“用户提供 Windows command line”的产品模型，不把 `Command` 改成 argv YAML 数组。

实现必须使用 Windows 正确的 command-line parsing/launch 语义，不能用简单 `Split(' ')`。

用户如果需要 shell 内建、重定向、管道等，必须显式写：

```yaml
Command: cmd.exe /c "..."
```

ShellCommand 不自动套 `cmd.exe /c`。

## Variable Expansion

按 `contracts/config.md`：

- `%DIR%`；
- Windows `%ENV%`。

展开失败不能执行部分 command。

## Failure

launch failure：

- 记录结构化错误；
- 可让 App 的 recent diagnostics 展示；
- 不向 Explorer 弹 modal MessageBox；
- 不 retry 任意用户命令。

## Security

ShellCommand 的本质允许执行用户自己配置的任意命令，因此“命令本身是否安全”不是产品替用户判断的事情。

但系统必须保证：

- 第三方 context menu metadata 永远不能变成 ShellCommand 用户命令；
- IPC client 不能提交任意 command string，只能提交 Broker 已签发 token；
- token 有用户/session 边界和 TTL；
- 管理器扫描到的注册表 command 只用于展示，不通过 ShellCommand 执行。
