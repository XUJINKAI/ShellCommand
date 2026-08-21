# Decision 0005: Context Menu Manager Is Reversible, Not Omnipotent

## Decision

V11 Context Menu Manager 提供：

- discovery；
- classification；
- details；
- reversible disable/restore where supported。

不承诺：

- 所有系统项都能禁用；
- 任意排序 Windows 11 modern context menu；
- 修改 AppX/PackagedCom 内部 registration；
- 删除第三方注册项作为“禁用”。

## Reason

Windows 右键菜单来源并不统一。

为了一个“万能开关”去写未公开/脆弱内部状态，会使工具本身成为系统污染源。

ShellCommand 的价值是：

> 尽可能看清来源，并对有稳定机制的项做可恢复控制。

## Packaged IExplorerCommand

如果能得到 CLSID，可以对 `Shell Extensions\Blocked` 做 per-user best-effort block；这不等同于改 package registration。

UI 必须把这种能力标记为可逆 blocking，不声称卸载/删除 extension。

## Rejected

### Rename/delete registry keys

第三方更新、系统修复和恢复都会变得不可预测。

### Edit AppRepository / PackagedCom

属于内部注册数据，不是本产品应该维护的契约。

## Reconsider When

Microsoft 提供正式的 per-extension enable/disable API。
