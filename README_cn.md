# ShellCommand

用 YAML 为 Windows 11 右键菜单添加常用操作。支持目录背景、文件、文件夹、多选及两层自定义分组。

## 安装与运行

依赖 **Windows 11 x64 和 .NET 10 Desktop Runtime x64**。发布使用 `PublishSingleFile=true`、`SelfContained=false`，不会捆绑 .NET 运行时；ZIP 中还包含原生扩展和身份资源。

解压完整 ZIP，运行 `ShellCommand.exe`，在「设置与任务」中启用。整套程序复制到 `%LOCALAPPDATA%\ShellCommand11\runner\<build-id>`；安装完成并关闭下载目录中的程序后，可删除下载副本。

安装只需界面操作，不要求签名、证书或命令行。点击「启用 / 修复」直接注册菜单；检测到旧版时可在界面确认替换。若 Windows 拒绝免签名注册，界面会显示完整错误，并在开发部署策略拒绝时提供 Windows 设置入口；开启开发者模式后返回重试。程序不自动修改系统策略。

## 配置

只支持 `version: 2`，不识别或迁移旧配置。

- 全局：`%LOCALAPPDATA%\ShellCommand11\config\global.shellcommand.yaml`
- 当前目录：`.shellcommand.yaml`，不向父目录搜索
- 本地与全局同 ID 时，本地完整替换全局；`enabled: false` 可屏蔽全局项
- `include` 按包含文件的目录解析，最多 8 文件/合计 1 MiB

```yaml
version: 2
menu:
  - id: terminal
    title: 在此打开终端
    icon: terminal
    run:
      exe: wt.exe
      args: ['-d', '${directory}']

  - id: git-status
    title: 查看 Git 状态
    when: {exists: .git}
    run:
      exe: git.exe
      args: [status]
      output: window

  - id: copy-selected
    title: 复制所选路径
    when: {context: selection}
    copy:
      values: '${selection.paths}'
      separator: "\r\n"
```

菜单页可编辑、保存并预览背景或选择项菜单。选择预览中的动作可查看实际程序、参数和工作目录；预览不执行命令。外部修改冲突不会被直接覆盖。错误配置保留上次有效菜单，并显示来源和原因。

条件支持 `all`、`any`、`not`、`exists`、`context` 和 `selection`。不存在/尚未准备好的目录事实不会误判为满足条件。第一次访问目录时可能只显示全局操作及设置，稍后重新打开即可显示准备完成的菜单。

动作支持 `run`、`script`、`open`、`copy`、`items`。`run.args` 必须是数组；不会自动套一层 cmd。`mode: each` 为每个选择项生成独立执行计划。跨目录选择没有默认 cwd，可显式使用 `${item.parent}`。

变量：`${directory}`、`${config_dir}`、`${app_dir}`、`${data_dir}`、`${env:NAME}`、`${selection.paths}`、`${item.path}`、`${item.parent}`。列表变量只能独占 args 元素或用于 copy.values。变量只展开一次，`$${` 表示字面量 `${`。

脚本使用显式 `powershell` / `pwsh` / `cmd`，脚本文本不展开上述变量，改读 `SC_DIRECTORY`、`SC_CONFIG_DIR`、`SC_SELECTION_JSON` 环境变量。管理员运行仅限 `run` 的 normal 输出，不支持同时自定义 env。

`output: window` 显示任务输出，输出长度有界；关闭窗口后任务继续，点击「停止」结束该任务进程树。普通 GUI 程序只记录成功启动，不宣称监控完整生命周期。

## 故障恢复

```text
ShellCommand.exe --uninstall
ShellCommand.exe --disable-integration
ShellCommand.exe --check-installation
```

卸载不依赖配置解析，保留 config 和系统菜单恢复记录。缺少 .NET 或 runner 损坏时可使用 ZIP 中的 `Uninstall.cmd`。不自动终止其他会话的进程或重启 Explorer。

系统菜单页独立扫描当前用户及机器级注册、现代打包菜单。只对支持的当前用户机制开放修改；现代菜单只读。损坏恢复日志会阻止修改；恢复前检查外部修改。COM 阻止可能同时影响该 CLSID 的多个菜单位置。

## 开发

- `packaging/scripts/Build.cmd Release`：构建
- `packaging/scripts/Test.cmd Release`：托管测试
- `packaging/scripts/Package.cmd Release`：少文件 ZIP
- `packaging/scripts/Smoke-Install.ps1`：干净 Win11 的安装、删除来源、卸载检查

架构和完整语法见 [设计文档](docs/redesign-v2.md)。Windows CI 包含原生命名管道故障注入、ASan、1 万次请求回收检查；这些检查不能替代真实 Explorer 的发布验收。
