# ShellCommand 11 产品设计

## 一句话定义

**ShellCommand 11 是一个 Windows 11 原生右键菜单工具：它根据当前目录中的简单 YAML 规则动态提供用户自定义命令，同时提供一个可逆的 Windows 右键菜单查看与管理界面。**

产品显示名：`ShellCommand 11`  
项目名：`ShellCommand11`  
首个版本：`11.0.0`

## 背景

旧 ShellCommand 面向 Windows 10，通过 SharpShell / `IContextMenu` COM 扩展在目录背景菜单中读取 `.shellcommand.yaml`，按目录内容动态生成命令。

ShellCommand 11 不把旧代码直接“兼容 Win11”，而是重新建立清晰边界：

```text
用户配置
    ↓
Core / Broker
    ↓
极薄 Win11 Explorer Adapter
```

ShellCommand 11 同时承担一个工程实验目标：验证“可独立开源 Core + Windows Adapter + 多宿主”的架构是否适合未来更大的 Windows 工具项目。这个实验目标不能反过来导致过度抽象。

## 用户价值

用户可以：

- 在任意目录放置 `.shellcommand.yaml`，为该目录定义右键命令；
- 定义全局命令；
- 根据当前目录是否存在 `.git`、`*.sln` 等文件/目录决定命令是否出现；
- 为命令配置名称、图标、工作目录和管理员权限；
- 在 Windows 11 新式一级右键菜单中使用 ShellCommand；
- 查看系统中的静态 verb、传统 COM 右键扩展和现代 packaged `IExplorerCommand`；
- 对支持安全、可逆控制的菜单项执行禁用/恢复；
- 查看来源、CLSID、注册位置、包等诊断信息；
- 验证 YAML 配置错误，而不让错误影响 Explorer 稳定性。

## 它不是什么

ShellCommand 11 **不是**：

- 通用启动器；
- 完整 Windows Shell 替代品；
- 文件管理器；
- 脚本 IDE；
- PowerToys 替代品；
- 用来任意重排 Windows 11 一级右键菜单的 Hack 工具；
- 用来修改 AppX/PackagedCom 内部数据库的注册表编辑器；
- 一个要求 OneQuick 存在才能工作的前端。

## 第一版支持范围

### ShellCommand 自己的菜单

V11.0 只注册：

```text
Directory\Background
```

即“目录空白处右键”。

不在 V11.0 中为以下上下文提供 ShellCommand 自定义命令：

```text
selected file
selected folder
Drive
Desktop special objects
```

未来可以扩展，但必须新增契约。

### Windows 版本

产品只面向 Windows 11。不会为了 Windows 10 保持旧 SharpShell 路径。

V11.0 首先支持 x64；架构不得阻碍未来 ARM64，但 V11.0 不要求 ARM64 发布物。

## 核心概念

```text
Config Source
├── Global Config
└── Directory Config

Directory Context
├── Working Directory
└── Directory Facts

Command Definition
├── Name
├── Command
├── Match
├── RunAsAdmin
└── Icon

Resolved Menu
├── Action
└── Separator
```

### Config Source

配置只是输入。YAML 不是核心模型。

### Directory Context

一次菜单解析只针对一个当前目录。V11.0 不向父目录递归搜索 `.shellcommand.yaml`。

### Match

Match 只用于便宜的当前目录存在性判断，不允许执行脚本、网络请求、Git 命令或递归扫描。

### Resolved Menu

只有匹配成功的命令进入最终菜单。未匹配命令 **隐藏**，不显示为 disabled。

## 菜单体验

Windows 11 右键一级菜单中出现一个 ShellCommand 入口：

```text
ShellCommand >
    Git Pull
    Open Terminal
    Copy Folder Path
    ...
```

只支持一层子命令。

ShellCommand 菜单不能因为 Broker/YAML 错误导致 Explorer 报错、卡死或崩溃。

当 Broker 完全不可用时，ShellCommand 根菜单仍可退化为一个最小静态项：

```text
Open ShellCommand 11
```

## 配置兼容原则

V11 的目录配置继续使用旧项目的核心字段：

```text
Name
Command
Match
RunAsAdmin
Icon
```

全局配置继续使用：

```text
GlobalCommands
Functions.CopyPath
Functions.EditGlobal
```

目标是让大多数旧配置无需修改即可迁移。

兼容的是**已文档化行为**，不保证旧实现中的偶然细节。例如旧版 `%DIR%` 曾把 `\` 转为 `/`，V11 将 `%DIR%` 定义为标准 Windows 绝对路径。

## 右键菜单管理器定位

管理器的核心目标是：

> 让用户看懂“这个菜单项从哪里来”，并在存在明确可逆机制时安全地禁用/恢复。

它必须区分：

```text
Static Verb
Legacy COM ContextMenuHandler
Packaged IExplorerCommand
Windows / protected / unknown
```

“发现”与“可修改”是两个不同能力。

V11 不承诺每一个菜单项都能被禁用。

## 稳定性优先级

产品优先级按以下顺序：

```text
Explorer 稳定性
    >
右键打开速度
    >
配置正确性与可诊断性
    >
功能丰富度
```

任何新功能如果要求在 Explorer UI 路径中做不可控工作，默认拒绝。

## 开源实验边界

ShellCommand 11 的模块边界应允许 `ShellCommand.Core` 在未来被独立复用或开源发布，但 V11 不提前创建 OneQuick Core，也不引入 OneQuick 类型、协议或依赖。

先让 ShellCommand 自己证明这套边界有效，再决定是否抽取共享 Core。
