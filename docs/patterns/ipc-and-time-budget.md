# Pattern: IPC and Time Budget

## Principle

Explorer 菜单构建是一条用户可感知 UI path，**deadline 比完整结果更重要**。

## V11 Engineering Budget

以下是 ShellCommand 自己的性能预算，不是 Microsoft API 保证：

```text
Explorer local work target      <= 2 ms
warm IPC round-trip target      <= 10 ms p95
ResolveMenu target              <= 15 ms p95
Explorer total hard deadline    30 ms
```

达到 hard deadline 后必须立即 fallback，不得继续等。

## Deadline Propagation

Explorer 创建一个绝对 deadline。

所有内部操作使用剩余时间：

```text
connect
write
read header
read payload
parse payload
```

禁止每个步骤各自 30ms 导致总时间叠加。

## No Retries on UI Path

V11 `EnumSubCommands`：

- 最多一次 connect；
- 最多一次 request/response；
- 失败直接 fallback。

Broker health recovery由 Broker/App 生命周期负责，不由 Explorer right-click retry 负责。

## Bounded Allocation

在读取 response 前验证 payloadLength。

Menu item 数、字符串长度均有上限。

不允许根据未验证长度进行 vector/string 大分配。

## Broker Soft Budget

Broker 应设置比 Explorer hard deadline 更小的 Resolve soft budget，以便有时间编码/传输 response。

例如可从 20ms 左右开始实现并通过 benchmark 调整；具体 soft number 是实现参数，不是外部契约。

## Slow First Parse

如果首次目录 YAML 无法在 soft budget 内完成：

- 本次可只返回 global + built-ins；
- background 完成解析；
- 下一次菜单使用缓存。

用户配置“最新”不能优先于 Explorer 响应性。

## Metrics

Broker 记录低成本聚合指标：

```text
resolve_count
resolve_duration histogram
timeout_count
cache_hit_count
first_parse_count
invalid_config_count
```

不要默认记录每次工作目录明文到遥测；本地 debug log 可按用户开启。
