> **v2 重设计契约（feat/redesign-v2）**：本分支按 [已确认设计](../redesign-v2.md) 分阶段重写；下文是重设计前的历史基线，不再对新实现构成兼容性要求。仅接受 YAML v2，不导入或迁移旧配置。实施与验证进度见 [P0](../p0-validation.md)。

# Config Contract

## 文件位置

### Directory Config

文件名固定：

```text
.shellcommand.yaml
```

只检查当前工作目录，不向父目录递归查找。

### Global Config

V11 默认位置：

```text
%LOCALAPPDATA%\ShellCommand11\config\global.shellcommand.yaml
```

安装目录中的模板只能作为初始模板，不是运行时配置源。

## Directory Config 格式

顶层是 command sequence：

```yaml
- Name: Open SourceTree Here(&S)
  Command: "%LocalAppData%/SourceTree/SourceTree.exe -f \"%DIR%\""
  Match: .git
  Icon: "%LocalAppData%/SourceTree/SourceTree.exe"

- Command: git pull
  Match: .git
  Icon: "%PROGRAMFILES%/Git/git-cmd.exe"

- Name: Create README.md
  Command: cmd /c copy nul README.md
  Match: ".git<&&>!README.md"

- Name: ---

- Name: Open Terminal
  Command: wt.exe -d "%DIR%"
```

## Global Config 格式

```yaml
GlobalCommands:
  - Name: Open Terminal
    Command: wt.exe -d "%DIR%"
    Icon: "%LOCALAPPDATA%/Microsoft/WindowsApps/wt.exe"

Functions:
  CopyPath: true
  EditGlobal: true
```

## Command 字段

| 字段 | 类型 | 必填 | 默认 | 含义 |
|---|---|---:|---|---|
| `Name` | string | 否 | `Command` | 菜单显示名 |
| `Command` | string | 动作项是 | 无 | 要执行的 Windows command line |
| `Match` | string | 否 | 总是匹配 | 当前目录存在性条件 |
| `RunAsAdmin` | bool | 否 | `false` | 通过 UAC 以管理员启动 |
| `Icon` | string | 否 | 无 | EXE/DLL/图标资源路径 |

V11.0 不增加嵌套 `Children`。Explorer 只支持 ShellCommand 根菜单下的一层动态命令。

## Separator

满足：

```yaml
- Name: ---
```

且 `Command` 为空/缺失时表示 separator。

规范化规则：

- 开头 separator 删除；
- 连续 separator 合并为一个；
- 结尾 separator 删除。

## Match 语法

V11 保留旧项目的简单语法。

### AND

使用：

```text
<&&>
```

例如：

```yaml
Match: ".git<&&>README.md"
```

两个条件都存在才匹配。

### NOT

term 以 `!` 开头表示反向：

```yaml
Match: "!.git"
```

### Wildcard

支持 Windows leaf-name wildcard：

```text
*
?
```

例如：

```yaml
Match: "*.sln"
```

### 限制

Match term：

- 只检查当前目录直接子项；
- 文件与目录都算存在；
- 不递归；
- 不执行 shell；
- 不访问网络；
- 不允许 `/`、`\\` 或 `..` 路径穿越；
- 不支持 OR；
- 不支持正则表达式；
- 不支持 Git 状态等外部事实。

不匹配的 command 不进入菜单，**不是 disabled item**。

## 变量展开

支持 Windows `%NAME%` 环境变量，并额外定义：

```text
%DIR%
```

`%DIR%` = 当前工作目录的完整 Windows 路径，例如：

```text
D:\Code\ShellCommand11
```

配置作者负责在命令中对包含空格的路径加引号：

```yaml
Command: wt.exe -d "%DIR%"
```

未定义的普通 Windows 环境变量保持系统展开语义；`%DIR%` 总是由 ShellCommand 替换。

## Icon

兼容：

```text
C:\Path\app.exe
C:\Path\icons.dll?3
%SystemRoot%\System32\Shell32.dll?70
```

`?index` 是 ShellCommand 配置语法，不要求 Explorer DLL 自己解析资源。Broker 应把 Icon 解析为 Explorer 可消费的 icon reference/cache。

Icon 失败不能使 command 消失；失败时使用无图标状态。

## Functions

V11.0 保留两个旧全局开关：

```yaml
Functions:
  CopyPath: true
  EditGlobal: true
```

### CopyPath

在 ShellCommand 菜单中增加 `Copy Folder Path`。

### EditGlobal

增加 `Edit Global Config`，动作由 Broker/App 处理。

缺失 `Functions` 等价于两个值均为 false。

## Global + Directory 合并顺序

最终菜单顺序：

```text
matched directory commands
separator（仅两侧都有内容时）
matched global commands
built-in functions
Open ShellCommand 11
```

其中 `Open ShellCommand 11` 始终作为正常 Broker 响应中的最后一个 action。

## YAML 子集

允许：

- mapping；
- sequence；
- string；
- boolean；
- integer（仅未来 schema 允许的字段）。

V11.0 禁止：

- custom tags；
- arbitrary type tags；
- anchors；
- aliases；
- merge keys；
- object graph type construction。

未知字段是 validation error，避免拼写错误静默失效。

## 输入上限

单文件：

```text
max bytes            256 KiB
max commands         100
max Match terms      64 per command
max scalar string    4096 UTF-16 code units equivalent
max YAML nesting     8
```

超过限制视为 invalid config。

## Error Contract

配置错误产生结构化诊断：

```text
source path
severity
code
message
line (if known)
column (if known)
```

示例 code：

```text
YAML_SYNTAX
UNKNOWN_FIELD
VALUE_TOO_LONG
TOO_MANY_COMMANDS
INVALID_MATCH
MISSING_COMMAND
INVALID_SEPARATOR
UNSUPPORTED_YAML_FEATURE
```

错误永远不跨越 Broker 请求边界抛给 Explorer。

## Last Known Good

如果某配置之前成功解析，之后被写坏：

```text
new invalid file
    ↓
report diagnostics
    ↓
do not replace cached config
    ↓
continue using last valid config
```

如果该文件从未有过有效版本，则忽略该 source；其他 source 仍正常工作。

## Legacy Compatibility

目标：旧 ShellCommand README/模板中的常见配置可直接使用。

明确改变：

- Match false 在 V11 一律隐藏，不保留旧版“某些 local item disabled、global item hidden”的不一致行为；
- `%DIR%` 返回标准 Windows 路径，不保留旧代码把反斜杠替换成 `/` 的偶然行为；
- YAML 错误不再传播到 Explorer。
