# Decision 0006: Preserve Legacy Config Syntax, Fix Undefined Quirks

## Decision

V11 保留旧 ShellCommand 的主要配置名和 Match 语法：

```text
.shellcommand.yaml
GlobalCommands
Functions
Name
Command
Match
RunAsAdmin
Icon
<&&>
!
*
?
---
%DIR%
```

但只兼容文档化意图，不复制旧实现中的不一致和偶然行为。

## Explicit V11 Changes

- Match false 始终隐藏 item；
- `%DIR%` 是标准 Windows absolute path；
- YAML parser failure 不传播；
- command line 不再用 naive split；
- input 有明确上限；
- unsupported YAML features 被拒绝。

## Reason

完全重做配置会让一个本来很小的工具失去延续性；完全复制旧 bug 又会把历史实现绑进新架构。

## Rejected

### Introduce a brand-new lowercase V11 schema immediately

没有足够产品收益，却增加迁移成本。

### Promise bit-for-bit legacy behavior

旧代码的 UI/解析行为本身不一致，不能作为稳定 contract。
