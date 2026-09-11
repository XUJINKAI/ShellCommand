# ShellCommand v2 — broker-ipc

本契约以[已确认的完整设计](../redesign-v2.md)为准。仅支持 `version: 2`，不保留旧字段、旧命令行语法或配置迁移。

安装根固定为 `%LOCALAPPDATA%\ShellCommand11`，config 与 runner 分离。Core 是不可变数据、条件、变量与执行计划；准备进程处理 YAML/目录/图标；Broker 的 Resolve 只读取内存，Invoke 入队后立即 ACK；用户动作在独立执行进程中运行；native 仅上下文、限时 IPC 和 COM 菜单映射。

用户已要求继续完成整体编码。实机检查是发布验收门槛，不阻塞后续开发。当前实现进度和实机检查见 `docs/p0-validation.md`；历史实现不构成兼容承诺。
