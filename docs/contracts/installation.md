# Installation Contract

## Goal

安装完成后，用户无需手工运行 `regsvr32`、编辑注册表或开发者模式命令，就能在 Windows 11 modern File Explorer context menu 使用 ShellCommand。

面向用户的发布形态是一个可直接解压的 `win-x64` ZIP。用户启动 ZIP 根目录中的 `ShellCommand.exe`，由该程序检测状态并执行安装、修复、卸载；PowerShell 脚本只保留给仓库开发和 CI 使用。

## Installed Components

至少安装：

```text
ShellCommand.exe
ShellCommand.Broker.exe
ShellCommand.Explorer.dll
supporting managed assemblies
package identity files / sparse package
product icon/resources
```

## Per-user Runtime State

```text
%LOCALAPPDATA%\ShellCommand11\
├── config\
│   └── global.shellcommand.yaml
├── state\
├── cache\
└── logs\
```

卸载默认不得删除用户配置，除非用户明确选择 purge。

## Package Identity

正式/开发安装都必须最终得到有效 package identity，并注册：

```text
windows.comServer
windows.fileExplorerContextMenus
```

Explorer COM Class 必须由 COM Surrogate 承载。

## Broker Startup

安装后必须保证：

- 用户登录时 Broker 自动启动；
- 当前安装会话中不必等到下次登录：installer 应启动 Broker；
- 每用户只运行一个 Broker；
- Broker 非管理员常驻。

具体 autostart 机制可以由 installer 选择，但必须可在卸载时完整移除。

## Install / Repair

Repair 必须幂等地确认：

- binary 存在；
- sparse package identity 注册正确；
- COM CLSID 与 manifest 一致；
- `Directory\Background` verb 注册存在；
- Broker autostart 正确；
- 当前 Broker 可连接。

Repair 不覆盖现有用户 global config。

## Upgrade

升级 V11.x 时：

1. 不删除用户配置；
2. 新 binary 先完整落盘；
3. 更新 package registration；
4. 重启 Broker 到新版本；
5. 标记 Explorer 可能需要 restart；
6. 不在 Explorer 正在加载旧 DLL 时强制覆盖导致不完整文件。

具体文件替换策略由 installer 实现，但必须避免半升级状态。

## Uninstall

卸载必须移除：

- ShellCommand 自己的 package registration；
- Explorer COM integration；
- Broker autostart；
- product binaries；
- ShellCommand 自己创建的临时/cache integration state。

默认保留：

```text
config\global.shellcommand.yaml
```

不得在卸载时“恢复”用户对第三方菜单做过的所有管理操作，除非产品明确提供并执行单独的 restore-all 步骤；卸载器不能猜测用户现在是否希望那些第三方菜单恢复。

## Purge

如果提供 `Purge user data`：

- 必须是明确选项；
- 删除 config/log/cache/state；
- 在删除 menu-manager journal 前，应提示存在尚未恢复的第三方菜单修改。

## Explorer Reload

安装/升级/卸载后如果 Shell 没立即反映：

- UI/installer 可以提示并提供 Restart Explorer；
- 不把“重启整台电脑”作为正常首选步骤。

## Dev Installation

仓库必须提供可重复的 dev build/package 流程；安装和卸载统一由 `ShellCommand.exe` 完成，使开发者可以：

```text
build
package
launch ShellCommand.exe
click Install / Repair
verify
click Uninstall when finished
```

开发入口使用 CMD，不要求手工查 GUID、运行 PowerShell 或编辑注册表。
