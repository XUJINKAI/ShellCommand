# Feature: Config Runtime

## Responsibility

把磁盘上的配置源稳定地转换为 Broker 可复用的、已验证 Core Model，并提供诊断与缓存。

## Owns

- global path；
- current-directory `.shellcommand.yaml` discovery；
- file fingerprint；
- bounded read；
- YAML Adapter invocation；
- diagnostics；
- Parsed Config cache；
- Last Known Good；
- explicit refresh from App；
- recently-used config invalidation。

## Does Not Own

- Explorer COM；
- process execution；
- context menu manager；
- UI rendering。

## Config Discovery

对于 working directory：

```text
<workingDirectory>\.shellcommand.yaml
```

只检查这一处。

Global 使用固定 V11 路径。

## Fingerprint

缓存至少基于：

```text
canonical path
file length
last write timestamp
```

实现可加 file ID/hash，但不得每次 Resolve 都对完整文件做 hash。

## Fast Path

已缓存且 fingerprint 未变：

```text
metadata check
    ↓
return parsed model
```

不重新读文件、不重新跑 YamlDotNet。

## Changed Config

已存在 Last Known Good 且发现 fingerprint 变化：

推荐：

1. 使用当前 LKG 完成本次延迟敏感 Resolve；
2. queue refresh；
3. refresh 成功后 atomic swap；
4. refresh 失败则继续 LKG 并记录 diagnostic。

App 的显式 `Validate/Refresh` 不受 Explorer 延迟预算限制，可以等待完整解析结果。

## First Seen Config

第一次看到一个 local config：

- 文件很小且解析能在内部预算内完成时可同步解析；
- 若超过 Broker 的 resolve soft budget，应放弃本次 local commands、继续返回 global/built-ins，并异步缓存；
- 绝不为了第一次解析突破 Explorer hard deadline。

## File Change Notifications

可以使用 FileSystemWatcher/ReadDirectoryChangesW 优化最近使用配置，但它只是 invalidation hint，不是 correctness source。

必须仍用 fingerprint 复核。

不要为整个磁盘建立递归 watcher。

## Last Known Good State

每个 source 独立：

```text
Global LKG
Directory A LKG
Directory B LKG
...
```

一个目录配置坏了不能污染 global 或其他目录。

## Diagnostics Retention

Broker 保存每个 active/recent source 的最近 diagnostic。

日志/诊断不得保存用户 command 的敏感 stdout/stderr，除非未来明确增加该功能。

## Invariants

- invalid new config never replaces valid cached config；
- cache entry 与 canonical path 唯一对应；
- config read 有 size limit；
- parser exceptions 必须在该 feature 边界转成 diagnostic；
- Explorer 不看到 parser exception。
