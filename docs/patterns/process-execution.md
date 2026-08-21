# Pattern: Process Execution

## Scope

统一用户 command、打开 App、需要 UAC 的动作启动语义。

## User Commands

用户 `Command` 是 Windows command line。

实现不得：

```text
Split(' ')
手工按第一个空格拆 exe/args
未经定义自动套 cmd.exe
```

必须使用与 Windows 命令行规则一致的解析/启动方式。

## Working Directory

所有用户 command 的 working directory = Resolve 时的目录。

工作目录在 action token 生成时冻结；点击时不从 Explorer 重新推断。

## Shell Features

如果用户需要：

```text
|
>
&&
cmd built-in
```

配置必须显式：

```yaml
Command: cmd.exe /c "..."
```

ShellCommand 自己不解释 shell metacharacters。

## Elevation

`RunAsAdmin: true`：

- 使用 Windows `runas` / ShellExecuteEx 等正式 UAC 机制；
- 不关闭 UAC；
- 不缓存管理员 token；
- 不把 Broker 自身变成长期 elevated process。

## Explorer Integration

用户命令绝不由 Explorer DLL 直接执行。

Explorer fallback `Open ShellCommand 11` 是例外，因为它只启动固定安装路径，不接受用户 command。

## Lifetime

V11 只负责成功启动，不负责：

- 捕获所有 stdout/stderr；
- terminal embedding；
- job dashboard；
- kill running command。

未来若增加这些能力必须新建 Feature/Contract。
