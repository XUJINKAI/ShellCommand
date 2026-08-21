# Pattern: Cache and Last Known Good

## Why

YAML 是用户可编辑文件，保存过程中短暂 invalid 很正常。右键菜单不能跟随半写文件抖动或崩溃。

## State Per Source

```text
path
fingerprint
lastKnownGoodModel?
lastKnownGoodFingerprint?
currentDiagnostic?
refreshState
lastAccess
```

## Atomic Replacement

只有完整经过：

```text
read
parse
schema validate
semantic validate
```

的新 model 才能替换 LKG。

替换在内存中原子完成；读请求只能看到 old 或 new，不能看到半构建状态。

## Invalid Refresh

```text
LKG exists + new invalid
    → keep LKG
    → store diagnostic

No LKG + invalid
    → source unavailable
    → store diagnostic
```

## Delete Is Different From Invalid

明确删除文件代表用户撤销该配置。

因此 delete 后不继续使用 LKG。

## Cache Eviction

Directory config 可能分布在大量目录。

Broker 可按 LRU/last access 清理长期未使用 entry/watchers。

Eviction 只影响性能，不影响行为；重新访问后重新发现。

不要保存无限增长的目录 cache。

## Concurrency

多个 Explorer 请求可能同时针对同一配置。

只允许一个 refresh 解析同一 fingerprint；其他请求继续读 LKG 或等待不超过自己的 soft budget。

避免 thundering herd。
