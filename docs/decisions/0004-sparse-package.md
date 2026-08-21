# Decision 0004: Sparse Package / External Location Identity

## Decision

ShellCommand 11 保持传统 Win32/.NET 安装布局，同时使用 sparse package（packaging with external location）获得 package identity 和 Windows 11 File Explorer extension registration。

## Reason

需要：

```text
windows.comServer
windows.fileExplorerContextMenus
modern Win11 context menu
```

但没有必要把整个 WPF/Broker 产品迁成 full MSIX。

Sparse package 允许保留现有 EXE/DLL 位置和 installer 选择。

## Manifest Essentials

至少包含：

```text
windows.comServer
  com:SurrogateServer
    ShellCommand.Explorer.dll

windows.fileExplorerContextMenus
  ItemType = Directory\Background
  Verb -> Explorer COM CLSID
```

## Rejected

### Registry-only legacy shell extension

只能满足 legacy menu，不满足 Windows 11 一级菜单目标。

### Full MSIX immediately

会把安装/更新问题与 Explorer 重构绑在一起，扩大实验范围。

## Reconsider When

未来如果完整 MSIX 分发明显简化更新/签名/Store，而不破坏用户配置与安装体验。
