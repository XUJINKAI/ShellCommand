# ShellCommand 11

[中文说明](README_cn.md)

ShellCommand 11 is a Windows 11 context-menu tool. It reads a small, documented YAML configuration in the current folder, combines it with a per-user global configuration, and exposes matching commands in the modern `Directory\Background` menu.

The repository contains the first working implementation baseline:

- `ShellCommand.Core`: platform-independent matching, menu resolution, variables, and immutable action models.
- `ShellCommand.Config.Yaml`: bounded YamlDotNet adapter with unknown-field, feature, schema, and semantic validation.
- `ShellCommand.Broker`: per-user pipe server, configuration cache/LKG, action tokens, command launch, and reversible menu-manager primitives.
- `ShellCommand.App`: WPF user entry point and install manager, published as `ShellCommand.exe`.
- `ShellCommand.Explorer`: native C++ `IExplorerCommand` adapter with bounded IPC and fallback behavior.
- `packaging`: sparse-package manifest and CMD build/test/portable-package entry points.

## End-user usage

The user-facing distribution is a portable `win-x64` ZIP. After extracting it, launch `ShellCommand.exe` from the ZIP root. The app detects the current integration state and provides Install/Repair, Uninstall, and Restart Explorer actions. Users do not need to run PowerShell scripts or edit the registry.

After installation, right-click the empty area of a folder to open the `ShellCommand` menu. V11.0 supports `Directory\Background` only.

## Build and test

```powershell
call packaging\scripts\Build.cmd Release
call packaging\scripts\Test.cmd Release
```

To build the native adapter and stage a complete developer package from a Visual Studio developer environment:

```powershell
call .\packaging\scripts\Build.cmd Release
```

Run the staged user app to install the integration:

```text
artifacts\Release\ShellCommand.exe
```

Install, uninstall, and Explorer restart are handled by the app. No PowerShell script is required.
The `Debug`/`Release` directory is a developer package and requires the .NET 10 desktop runtime. For a self-contained package, use `Package.cmd` below.

## Build a portable ZIP

```powershell
call .\packaging\scripts\Package.cmd Release
```

The only user-facing output is `artifacts\ShellCommand-11.0.0-win-x64.zip`. Its root contains the self-contained `ShellCommand.exe`, Broker, native Explorer adapter, and sparse-package manifest. The temporary staging directory is removed automatically.

## Configuration

Directory configuration is `.shellcommand.yaml` and uses the documented legacy-compatible fields:

```yaml
- Name: Open Terminal
  Command: wt.exe -d "%DIR%"
  Match: .git<&&>!README.md
```

Global configuration is `%LOCALAPPDATA%\ShellCommand11\config\global.shellcommand.yaml`. See [the configuration contract](docs/contracts/config.md) for limits, diagnostics, separators, wildcard matching, and merge order.

The native Explorer adapter never parses YAML, loads CLR, runs user commands, scans the registry, or accesses the network. All dynamic work is bounded behind the per-user Broker pipe; failures degrade to `Open ShellCommand 11` without UI error dialogs.
