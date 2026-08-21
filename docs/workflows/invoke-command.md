# Workflow: Invoke Command

## Trigger

用户点击 ShellCommand 动态 action。

## Flow

```text
Explorer child Invoke(token)
        │
        ▼
Named Pipe InvokeRequest
        │
        ├── Broker unavailable → safe failure
        │
        ▼
Broker validates token
        │
        ├── missing/expired → reject
        │
        ▼
Broker marks token accepted
        │
        ▼
InvokeAccepted
        │
        ▼
Explorer returns immediately
        │
        ▼
Broker asynchronously launches action
        │
        ├── normal → current user launch
        └── admin → UAC runas
```

## Token Semantics

Token 绑定 Resolve 时生成的 action plan。

配置在菜单打开后变化，不改变这个 token 已绑定的 command。

Broker 重启使旧 token 失效；不得按“同位置 command”猜测恢复。

## Duplicate Click

Broker 应在 accept 阶段原子标记 token，避免同一个 token 因 IPC retry 被无意执行多次。

V11 推荐 token successful accept 后不可再次 accept。

## Execution Failure

`InvokeAccepted` 之后的 process launch failure：

- 写 diagnostic/log；
- 不改变 Explorer 返回；
- App 可以显示 recent error；
- 不自动 retry。

## UAC Cancel

用户取消 UAC 是正常可诊断结果，不是 Broker crash。
