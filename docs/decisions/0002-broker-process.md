# Decision 0002: Per-user Broker Process

## Decision

复杂逻辑放入每用户一个 `ShellCommand.Broker.exe`。

V11.0 Broker 在用户登录后启动并保持运行，会话期间不 idle-exit。

## Reason

Explorer menu 需要可预测的 warm latency。

Broker 承担：

- YAML；
- cache；
- Match IO；
- command execution；
- context menu manager。

这样 Explorer DLL 保持最小故障面。

## Why Resident

如果每次右键冷启动 .NET + YamlDotNet，首次菜单延迟不可预测。

常驻 Broker 可以：

- 复用 parsed config；
- 保持 Named Pipe ready；
- 避免 Explorer UI path 启动进程。

Broker 必须事件驱动，因此“常驻”不等于轮询耗 CPU。

## Rejected

### Parse YAML in Explorer DLL

扩大 Shell failure surface，并把用户输入 parser 放到 UI path。

### Start Broker on every right click

cold start latency 与进程启动失败都会污染 Explorer 体验。

### Windows Service

不需要 machine-level 服务或 system privilege；用户配置/菜单属于 user session。

## Reconsider When

实测常驻内存成本明显不可接受，并且可以通过 NativeAOT/idle-exit/共享 snapshot 在不增加 Explorer latency 的情况下替代。
