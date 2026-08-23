# Packaging

`packaging` contains only the Windows package manifest and developer build entry points.

| Path | Purpose |
|---|---|
| `manifest/AppxManifest.xml` | Sparse package identity, COM Surrogate registration, and the `Directory\Background` Explorer menu registration. |
| `scripts/Build-Solution.cmd` | Finds the Visual Studio toolchain and builds the complete `ShellCommand.sln`, including the managed projects and `ShellCommand.Explorer.vcxproj`. |
| `scripts/Build.cmd` | Builds the complete solution and stages one complete developer package under `artifacts\Debug` or `artifacts\Release`. It does not copy nested publish/runtime directories. |
| `scripts/Create-Zip.ps1` | Creates and validates a standard ZIP with root entries; it avoids the `./` entry names produced by some `tar.exe` versions. |
| `scripts/Uninstall.cmd` | Emergency per-user integration removal when the Explorer extension prevents the WPF app from being used. |
| `scripts/Package.cmd` | Publishes self-contained x64 App/Broker binaries into a temporary working directory, adds the manifest/assets, validates the ZIP root, and creates `artifacts\ShellCommand-11.0.0-win-x64.zip`. Temporary directories are removed. |
| `scripts/Test.cmd` | Runs the solution test suite. |

Installation, repair, uninstall, and Explorer restart are intentionally not packaging scripts. They are user actions handled by `ShellCommand.exe`.

Typical developer commands:

```cmd
call packaging\scripts\Build.cmd Release
call packaging\scripts\Test.cmd Release
call packaging\scripts\Package.cmd Release
```
