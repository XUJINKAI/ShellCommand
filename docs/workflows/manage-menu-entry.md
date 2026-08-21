# Workflow: Manage Installed Menu Entry

## Trigger

用户在 Context Menu Manager 对一个可修改 entry 选择 Disable 或 Restore。

## Disable

```text
User clicks Disable
        │
        ▼
Re-scan/revalidate entry identity
        │
        ├── source changed → abort + refresh UI
        │
        ▼
Determine supported strategy
        │
        ├── none → read-only; no write
        │
        ▼
Capture exact previous registry state
        │
        ▼
Persist journal record (prepared)
        │
        ▼
Apply one bounded registry mutation
        │
        ├── permission required → explicit UAC action
        │
        ▼
Verify registry write
        │
        ▼
Mark journal committed
        │
        ▼
UI = Pending Explorer Restart
```

## Restore

```text
User clicks Restore
        │
        ▼
Find committed ShellCommand journal record
        │
        ├── none → do not guess
        │
        ▼
Verify target identity has not been replaced
        │
        ▼
Restore exact previous existence/type/data
        │
        ▼
Mark journal restored
        │
        ▼
Pending Explorer Restart
```

## External Changes

如果第三方 installer 在 ShellCommand 禁用后改变了同一个 registration：

- Restore 不能盲目覆盖；
- 检测到冲突时停止并显示 `External change detected`；
- 用户可查看 details；
- 不提供“强制恢复”直到有单独明确设计。

## Restart Explorer

用户可选择：

```text
Restart Now
Later
```

Restart Now 必须只针对 Explorer 生命周期，不重启整机。

## Failure

registry write / elevation failure：

- journal 不能错误标记 committed；
- UI 回到原状态；
- 保留 diagnostic；
- 不进行第二种“猜测策略”。
