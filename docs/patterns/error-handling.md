# Pattern: Error Handling

## Error Categories

```text
User Input Error
External State Error
Expected Runtime Failure
Programming Error
Boundary Corruption
```

不同错误不能全部变成 `Exception -> log -> continue`。

## User Input Error

例如：

- YAML syntax；
- invalid Match；
- unknown field。

处理：

```text
structured Diagnostic
no exception outside config boundary
LKG policy
```

## External State Error

例如：

- file disappeared；
- registry access denied；
- package uninstalled during scan。

处理：

- 返回明确 result；
- 允许重新扫描；
- 不假设系统状态稳定。

## Expected Runtime Failure

例如：

- UAC cancelled；
- executable not found；
- pipe timeout。

处理为可诊断状态，不 crash。

## Programming Error

例如 impossible invariant 被破坏。

在 Core/Broker 测试/Debug 中尽快失败；在 Explorer boundary 仍必须转换成安全失败。

不要通过大量 silent catch 掩盖 bug。

## Result Model

业务层优先使用：

```text
Result<T, Error>
Diagnostic[]
```

而不是把所有可预期失败都做 exception control flow。

## Log Ownership

Broker 负责详细本地日志。

日志必须：

- bounded rotation；
- 默认不写敏感 command output；
- 不阻塞 Explorer deadline；
- 失败时不影响主功能。

## User-visible Error

App 显示 actionable error：

```text
what failed
which source
where (line/column/path)
what state remains active (e.g. using last known good)
```

Explorer 不显示业务错误。
