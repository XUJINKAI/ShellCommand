# References

以下资料用于实现校验，不是产品契约本身。

## Windows 11 Explorer

- Microsoft Learn — Add a File Explorer context menu command to a packaged desktop app  
  https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer

  关键事实：Windows 11 现代菜单使用 native `IExplorerCommand` + app identity；支持 `Directory\\Background`；菜单构建方法必须快速；长工作应放到 `Invoke` 之后。

- Microsoft Learn — Integrate your desktop app with Windows using packaging extensions  
  https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions

  关键事实：`windows.comServer`、`com:SurrogateServer`、`windows.fileExplorerContextMenus` 的 manifest 注册。

- Microsoft Learn — IExplorerCommand  
  https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand

- Microsoft Learn — Grant package identity by packaging with external location manually  
  https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps

## Shell verbs / context handlers

- Microsoft Learn — Creating Shortcut Menu Handlers  
  https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers

- Microsoft Learn — Best Practices for Shortcut Menu Handlers and Multiple Verbs  
  https://learn.microsoft.com/en-us/windows/win32/shell/verbs-best-practices

- Microsoft Docs source — Registering Shell Extension Handlers  
  https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/shell/reg-shell-exts.md

## Real-world native implementation

- Microsoft — vscode-explorer-command  
  https://github.com/microsoft/vscode-explorer-command

  可参考其 native DLL、WRL/WIL、`IExplorerCommand`、`DllCanUnloadNow` 和 `ShellExecuteW` 的边界处理，但不要照抄其静态菜单产品模型。

## Legacy ShellCommand

- https://github.com/XUJINKAI/ShellCommand

旧项目仅供了解原有用途。v2 不支持旧配置，也不提供迁移器。

旧代码架构（SharpShell、WinForms ContextMenuStrip、.NET Framework 4.7.2）不是 V11 实现参考。
