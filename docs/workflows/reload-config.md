# Workflow: Reload Config

## Trigger

配置发生变化，来源可以是：

- 用户用外部编辑器保存；
- App 请求 Validate/Refresh；
- Broker Resolve 时发现 fingerprint 变化；
- watcher 发出 invalidation hint。

## Flow

```text
Change detected
    │
    ▼
Debounce/coalesce short burst if event-driven
    │
    ▼
Bounded file read
    │
    ├── file too large → Diagnostic; keep LKG
    ├── read failure → Diagnostic; keep LKG
    │
    ▼
YAML parse
    │
    ├── syntax failure → Diagnostic; keep LKG
    │
    ▼
Schema + semantic validation
    │
    ├── invalid → Diagnostic; keep LKG
    │
    ▼
Build immutable parsed config
    │
    ▼
Atomic cache swap
    │
    ▼
Clear active error for this source
```

## Partial Writes

编辑器可能产生：

```text
truncate
write partial
write rest
rename
```

因此 watcher event 不能直接等价于“这是完整新配置”。

建议对 watcher-driven refresh 做短 debounce（例如 100–250ms 的实现级参数），但 correctness 依赖 parse/validation，不依赖 debounce。

## Delete

配置文件被删除：

- source 变为 absent；
- 不继续永久使用已删除文件的 LKG；
- 下一次菜单按“无该 source”处理；
- 清理相关 watcher/cache entry 可延迟进行。

## App Validate

`Validate` 必须验证磁盘当前内容，不返回 LKG 作为“valid”。

如果当前文件坏了：

```text
Runtime menu may still use LKG
App validation = invalid
```

UI 必须能表达这个差异。
