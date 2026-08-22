# Packaging

`packaging` contains only the Windows package manifest and developer build entry points.

| Path | Purpose |
|---|---|
| `manifest/AppxManifest.xml` | Sparse package identity, COM Surrogate registration, and the `Directory\Background` Explorer menu registration. |
| `scripts/Build-Solution.cmd` | Finds the Visual Studio toolchain and builds the complete `ShellCommand.sln`, including the managed projects and `ShellCommand.Explorer.vcxproj`. |
| `scripts/Build.cmd` | Builds the complete solution and stages a developer build under `artifacts\Debug` or `artifacts\Release`. |
| `scripts/Package.cmd` | Publishes self-contained x64 App/Broker binaries, adds the manifest/assets, and creates `artifacts\ShellCommand-11.0.0-win-x64.zip`. |
| `scripts/Test.cmd` | Runs the solution test suite. |

Installation, repair, uninstall, and Explorer restart are intentionally not packaging scripts. They are user actions handled by `ShellCommand.exe`.

Typical developer commands:

```cmd
call packaging\scripts\Build.cmd Release
call packaging\scripts\Test.cmd Release
call packaging\scripts\Package.cmd Release
```
