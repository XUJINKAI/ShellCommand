# ShellCommand

A YAML-driven Windows 11 context menu tool. Supports folder backgrounds, selected files/folders, multiple selections, structured commands and nested menus.

Requires Windows 11 x64 and **.NET 10 Desktop Runtime x64**. Managed executables use framework-dependent single-file publishing; the distribution also contains a small native COM extension and package identity resources.

Installed files live under `%LOCALAPPDATA%\ShellCommand11\runner\<build-id>`. Global configuration lives under `config\global.shellcommand.yaml`; local configuration is `.shellcommand.yaml` in the current directory. Only YAML v2 is supported; no legacy conversion is performed.

The management window includes a menu editor/preview, task results and reversible current-user context menu controls. Explorer never parses YAML or executes user commands. The broker resolves immutable snapshots and acknowledges queued actions before launching an executor.

Extract the complete ZIP, open ShellCommand.exe and click Enable / Repair. Installation registers the manifest directly, without a signing certificate or command-line steps. The UI offers replacement of old integration and displays installation errors. If Windows rejects development deployment, it provides a Settings shortcut; enable Developer Mode there and retry. System policy is never changed automatically. Real Windows 11 Explorer testing remains necessary.

See the [Chinese usage guide](README_cn.md), [design](docs/redesign-v2.md) and [validation notes](docs/p0-validation.md).
