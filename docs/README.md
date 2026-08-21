# ShellCommand 11 文档索引

ShellCommand 11 是旧 ShellCommand 的 Windows 11 重构版，产品版本从 **11.0.0** 开始。

本文档按“产品 → 契约 → 架构 → 功能 → 工作流 → 模式 → 决策 → 实现验证”的层级组织。AI 不应一次性把全部文档塞进上下文，而应按当前任务读取相关文件。

## 文档地图

```text
Product
  design.md
      ↓
External / System Contracts
  contracts/
      ↓
Architecture
  architecture.md
      ↓
Features
  features/
      ↓
Workflows
  workflows/
      ↓
Patterns
  patterns/
      ↓
Decisions
  decisions/
      ↓
Implementation & Verification
  development.md
```

## 顶层文档

| 文件 | 回答的问题 |
|---|---|
| `design.md` | ShellCommand 11 是什么、不是什么、产品边界是什么 |
| `architecture.md` | 系统分几层、进程怎么分、谁允许依赖谁 |
| `development.md` | 用什么技术栈、怎么构建、测试、按什么顺序实现 |
| `references.md` | Win11 Shell 官方资料和旧项目参考 |

## Contracts

| 文件 | 内容 |
|---|---|
| `contracts/config.md` | `.shellcommand.yaml` 与全局 YAML 的外部格式契约 |
| `contracts/config.schema.json` | 可机器校验的 V11 配置 Schema（用于全局/目录配置的核心字段） |
| `contracts/broker-ipc.md` | Explorer ↔ Broker 二进制 IPC 协议 |
| `contracts/explorer.md` | Win11 Explorer 中 ShellCommand 菜单必须怎样表现 |
| `contracts/ui.md` | ShellCommand App 的界面状态与交互契约 |
| `contracts/installation.md` | 安装、升级、修复、卸载的外部契约 |

## Features

| 文件 | 内容 |
|---|---|
| `features/custom-menu.md` | 动态 ShellCommand 菜单的职责与规则 |
| `features/config-runtime.md` | 配置发现、解析、缓存、诊断 |
| `features/command-execution.md` | 命令执行与提权 |
| `features/context-menu-manager.md` | 查看/启用/禁用 Windows 右键菜单项 |

## Workflows

| 文件 | 场景 |
|---|---|
| `workflows/build-menu.md` | 用户右键时如何得到菜单 |
| `workflows/reload-config.md` | YAML 变化后如何安全刷新 |
| `workflows/invoke-command.md` | 点击菜单后如何执行命令 |
| `workflows/manage-menu-entry.md` | 禁用/恢复第三方菜单项 |
| `workflows/install-integration.md` | Sparse Package / COM / Broker 的安装注册流程 |

## Patterns

| 文件 | 统一规则 |
|---|---|
| `patterns/explorer-boundary.md` | Explorer/COM 边界的稳定性规则 |
| `patterns/ipc-and-time-budget.md` | Named Pipe 与菜单延迟预算 |
| `patterns/cache-and-last-known-good.md` | 缓存、Last Known Good、配置原子替换 |
| `patterns/error-handling.md` | Result、异常边界、日志与用户可见错误 |
| `patterns/process-execution.md` | CreateProcess/ShellExecute、参数、提权与异步执行 |

## Decisions

`decisions/` 保存“为什么”，防止未来 AI 把已经明确拒绝的方案重新引入。

## Generated

`generated/` 只存机器生成结果。目录中的 `README.md` 说明预期生成物；不要手工维护生成内容。

## 任务阅读建议

### 做 Explorer 扩展

读：

```text
AGENTS.md
architecture.md
contracts/explorer.md
contracts/broker-ipc.md
workflows/build-menu.md
patterns/explorer-boundary.md
patterns/ipc-and-time-budget.md
decisions/0001-native-iexplorercommand.md
```

### 做 YAML / Core

读：

```text
AGENTS.md
contracts/config.md
features/config-runtime.md
features/custom-menu.md
patterns/cache-and-last-known-good.md
decisions/0003-yaml-is-adapter.md
```

### 做右键菜单管理器

读：

```text
AGENTS.md
features/context-menu-manager.md
workflows/manage-menu-entry.md
contracts/ui.md
decisions/0005-context-menu-manager-scope.md
```
