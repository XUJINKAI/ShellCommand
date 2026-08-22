# ShellCommand 11

[English](README.md)

ShellCommand 11 是一个面向 Windows 11 的右键菜单工具。它读取当前目录中的 `.shellcommand.yaml`，结合当前用户的全局配置，将符合条件的命令显示在 Windows 11 现代右键菜单的 `Directory\Background` 入口中。

## 项目结构

- `ShellCommand.Core`：跨平台核心模型、Match 规则、变量展开和菜单解析。
- `ShellCommand.Config.Yaml`：受限 YAML 适配器、字段校验和结构化诊断。
- `ShellCommand.Broker`：每用户 Broker、Named Pipe、配置缓存、Last Known Good、action token、命令执行和右键菜单管理。
- `ShellCommand.App`：WPF 用户入口、状态检测和安装管理界面，发布后的程序名为 `ShellCommand.exe`。
- `ShellCommand.Explorer`：原生 C++ `IExplorerCommand` 扩展，仅负责 Explorer 边界适配和限时 IPC。
- `packaging`：Sparse Package manifest，以及开发构建、测试和绿色 ZIP 打包入口。

## 面向用户的使用方式

发布包是一个绿色 ZIP。解压后直接运行根目录中的：

```text
ShellCommand.exe
```

程序会自动检测安装状态，并提供“安装 / 修复”“卸载”和“重启 Explorer”按钮。普通用户不需要运行 PowerShell、编辑注册表或执行其他安装命令。安装和卸载默认保留用户配置。

安装完成后，在任意目录的空白处右键即可看到 `ShellCommand` 菜单。V11.0 只支持目录空白处右键，不处理选中的文件或文件夹。

## 开发者构建与测试

```powershell
call packaging\scripts\Build.cmd Release
call packaging\scripts\Test.cmd Release
```

项目使用 .NET 10。YAML 解析只存在于 `ShellCommand.Config.Yaml`，Core 不依赖 YamlDotNet、WPF、COM、Registry 或 Windows UI。

## 生成绿色 ZIP

在具备 Windows SDK 和 MSVC C++ 工具链的 Visual Studio 开发环境中运行：

```powershell
call .\packaging\scripts\Package.cmd Release
```

输出文件：

```text
artifacts\ShellCommand-11.0.0-win-x64.zip
```

该 ZIP 包含自包含的 `ShellCommand.exe`、Broker、Explorer 扩展和 sparse package manifest，可复制到其他 Windows 11 x64 电脑后直接运行。

生成 ZIP 后，运行 `artifacts\ShellCommand11-portable\ShellCommand.exe`，点击应用内的“安装 / 修复”完成开发安装；卸载和重启 Explorer 也在应用内完成。

## 配置示例

目录配置文件名固定为 `.shellcommand.yaml`：

```yaml
- Name: Open Terminal
  Command: wt.exe -d "%DIR%"

- Name: Git Status
  Command: git status
  Match: .git

- Name: Create README.md
  Command: cmd.exe /c copy nul README.md
  Match: ".git<&&>!README.md"
```

全局配置位于：

```text
%LOCALAPPDATA%\ShellCommand11\config\global.shellcommand.yaml
```

例如：

```yaml
GlobalCommands:
  - Name: Open Terminal
    Command: wt.exe -d "%DIR%"

Functions:
  CopyPath: true
  EditGlobal: true
```

支持的主要字段包括：

- `Name`：菜单显示名称；缺失时使用 `Command`。
- `Command`：Windows command line。ShellCommand 不会自动添加 `cmd.exe /c`。
- `Match`：当前目录直接子项的存在性条件，支持 `<&&>`、`!`、`*` 和 `?`。
- `RunAsAdmin`：是否通过 UAC 以管理员身份启动。
- `Icon`：EXE 或 DLL 图标路径，可使用 `?index` 指定资源索引。
- `Name: ---`：菜单分隔线。

`%DIR%` 会展开为当前工作目录的完整 Windows 路径。普通环境变量也支持展开，例如 `%LOCALAPPDATA%` 和 `%PROGRAMFILES%`。

## 稳定性与安全边界

- Explorer 扩展不解析 YAML、不加载 CLR、不执行用户命令、不访问网络。
- Explorer 菜单路径使用有上限的 Named Pipe 请求和硬超时。
- Broker 不可用、配置错误或 IPC 响应损坏时，菜单退化为 `Open ShellCommand 11`，不会弹出错误对话框。
- Match 不执行脚本、不访问网络、不递归扫描目录。
- action token 绑定不可变执行计划，默认有效期为两分钟，成功接收后不可重复使用。
- 第三方右键菜单管理只使用可逆策略，并记录修改前的注册表状态。
- 不修改 AppX/PackagedCom 内部数据库，也不提供 Windows 11 一级菜单任意排序功能。

详细行为以 `docs/contracts`、`docs/features` 和 `docs/workflows` 中的文档为准。
