# ShellCommand 11

[中文说明](README_cn.md)

ShellCommand 11 is a Windows 11 context-menu tool. It reads a small, documented YAML configuration in the current folder, combines it with a per-user global configuration, and exposes matching commands in the modern `Directory\Background` menu.

The repository contains the first working implementation baseline:

- `ShellCommand.Core`: platform-independent matching, menu resolution, variables, and immutable action models.
- `ShellCommand.Config.Yaml`: bounded YamlDotNet adapter with unknown-field, feature, schema, and semantic validation.
- `ShellCommand.Broker`: per-user pipe server, configuration cache/LKG, action tokens, command launch, and reversible menu-manager primitives.
- `ShellCommand.App`: minimal WPF diagnostics/configuration shell.
- `ShellCommand.Explorer`: native C++ `IExplorerCommand` adapter with bounded IPC and fallback behavior.
- `packaging`: sparse-package manifest and repeatable development install/uninstall scripts.

## Build and test

```powershell
dotnet restore ShellCommand11.sln
dotnet build ShellCommand11.sln
dotnet test ShellCommand11.sln --no-build
```

To build the native adapter and stage a development package from a Visual Studio developer environment:

```powershell
.\packaging\scripts\Install-Dev.ps1
.\packaging\scripts\Restart-Explorer.ps1
```

Remove only the ShellCommand integration with:

```powershell
.\packaging\scripts\Uninstall-Dev.ps1
```

The uninstall script preserves `%LOCALAPPDATA%\ShellCommand11\config` by default. Use `-PurgeUserData` only when explicitly deleting user data is intended.

## Configuration

Directory configuration is `.shellcommand.yaml` and uses the documented legacy-compatible fields:

```yaml
- Name: Open Terminal
  Command: wt.exe -d "%DIR%"
  Match: .git<&&>!README.md
```

Global configuration is `%LOCALAPPDATA%\ShellCommand11\config\global.shellcommand.yaml`. See [the configuration contract](docs/contracts/config.md) for limits, diagnostics, separators, wildcard matching, and merge order.

The native Explorer adapter never parses YAML, loads CLR, runs user commands, scans the registry, or accesses the network. All dynamic work is bounded behind the per-user Broker pipe; failures degrade to `Open ShellCommand 11` without UI error dialogs.
