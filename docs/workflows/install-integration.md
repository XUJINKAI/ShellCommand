# Workflow: Install / Register Windows Integration

## Install

```text
Build/copy complete binaries
        │
        ▼
Prepare sparse package identity
        │
        ▼
Register package
        │
        ├── failed → rollback package step; do not claim installed
        │
        ▼
Verify COM class + context menu manifest mapping
        │
        ▼
Register Broker autostart
        │
        ▼
Start Broker for current user
        │
        ├── health check failed → installation diagnostic
        │
        ▼
Create global config from template only if absent
        │
        ▼
Offer/perform user-approved Explorer restart
        │
        ▼
Verify modern menu entry
```

## Important Ordering

不要先注册一个指向尚未完整写入 DLL 的 COM class。

Binary 应先完整就位，再让 Shell 可激活它。

## Repair

```text
Read installed layout
  ↓
Validate package identity/manifest
  ↓
Validate Explorer DLL exists
  ↓
Validate Broker startup
  ↓
Validate Broker pipe
  ↓
Repair only missing/broken integration
```

Repair 不重置 global YAML。

## Uninstall

```text
Stop accepting new Broker actions
        │
        ▼
Unregister sparse package / Shell integration
        │
        ▼
Stop Broker
        │
        ▼
Remove autostart
        │
        ▼
Remove binaries when no longer loaded
        │
        ▼
Preserve user config by default
        │
        ▼
Offer Explorer restart
```

如果 Explorer/Surrogate 仍持有 DLL 导致文件锁：

- 先注销 integration；
- 请求/提示 Explorer reload；
- 不通过无限重试或粗暴系统重启隐藏卸载设计问题。

## Verification

安装完成验证：

- Broker Ping OK；
- package identity present；
- modern `Directory\Background` menu present；
- `Show more options` 不是唯一入口；
- fallback 在 Broker kill 后可用；
- uninstall 后 ShellCommand modern entry 消失。

安装仅从界面直接注册 manifest，不检查签名身份包；完整当前约定见 [安装契约](../contracts/installation.md)。
