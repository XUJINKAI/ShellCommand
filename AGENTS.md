# AGENTS.md

ShellCommand 11 是一个面向 Windows 11 的轻量右键菜单工具，产品版本为 **11.0.0**。

本文件是 AI Agent 的项目入口，不替代详细设计文档。

## 开工前必须阅读

按任务读取最少必要上下文：

1. 所有任务先读 `docs/design.md` 与 `docs/architecture.md`。
2. 修改外部行为前读对应 `docs/contracts/`。
3. 实现具体功能前读对应 `docs/features/`。
4. 涉及跨模块动作链时读对应 `docs/workflows/`。
5. 遇到通用实现问题时读 `docs/patterns/`。
6. 准备改变既有架构选择前读 `docs/decisions/`。
7. 构建、测试、目录、技术栈要求见 `docs/development.md`。

导航见 `docs/README.md`。

## 不可违反的系统约束

以下规则高于局部实现便利：

- Explorer 扩展 **不得解析 YAML**。
- Explorer 扩展 **不得加载 CLR/.NET Runtime**。
- Explorer 扩展 **不得执行用户命令**。
- Explorer 扩展 **不得访问网络**。
- Explorer 菜单构建路径必须有硬超时；Broker 超时或异常时必须 fail closed / fail invisible，不能拖慢或拖垮 Explorer。
- YAML 只是配置适配器，不是 Core。
- `ShellCommand.Core` 不得依赖 WPF、COM、Registry、MSIX、Named Pipe、YamlDotNet 或其他 Windows UI/部署实现。
- 复杂工作放在 `ShellCommand.Broker`；`ShellCommand.Explorer` 只负责 COM、路径提取、IPC、菜单 DTO 映射和 Invoke 转发。
- 不允许修改 Windows 的 AppX/PackagedCom 注册数据库来“管理”第三方菜单。
- 对第三方右键菜单的任何禁用操作必须可逆，并保存修改前状态。
- 不实现“任意排序 Windows 11 一级右键菜单”。

## 文档权威顺序

发生冲突时按以下优先级处理：

```text
contracts / explicit invariants
        >
architecture
        >
features / workflows
        >
patterns
        >
existing code
```

旧 ShellCommand 仓库是迁移参考，不是新架构的权威来源。

## 修改文档的规则

- 不要为了让实现“看起来合理”而偷偷改产品契约。
- 若需求要求改变稳定契约，应先修改相应 Contract/Decision，再改代码。
- 新的跨模块规则应进入文档，不要只留在代码注释。
- `docs/generated/` 只放工具生成内容，禁止手工维护生成结果。
- 不为简单 helper、普通 CRUD 或显而易见代码新增独立设计文档。

## 实现原则

- 优先实现最小、可测试、可回滚的版本。
- 不预先抽象 OneQuick；ShellCommand 11 只需保证 Core 边界可复用。
- 不引入常驻轮询。Broker 允许常驻，但必须事件驱动。
- 不在 Explorer UI 路径中做阻塞磁盘扫描、网络、进程启动、复杂日志初始化或配置解析。
- 任何跨进程输入都视为不可信，必须做长度、版本和枚举值校验。
- 所有进程边界必须捕获异常，不允许异常越过 COM ABI 或 Named Pipe 请求边界。

## 完成定义

一个任务只有在以下条件都满足时才算完成：

- 对应 Contract 没被破坏；
- 单元测试覆盖核心规则；
- 跨进程行为有集成测试；
- Explorer 失败路径已验证；
- 新增的重要不变量已进入测试或静态检查；
- 文档与实现一致；
- 没有引入未说明的后台常驻、提权、注册表写入或兼容性行为。
