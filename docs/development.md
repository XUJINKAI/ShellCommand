# ShellCommand 11 开发与验证

## 技术栈

### Managed

- .NET 10 LTS
- C#
- WPF：ShellCommand.App
- YamlDotNet：仅 `ShellCommand.Config.Yaml`
- xUnit（或等价 .NET 测试框架）

### Native

- C++20 或更新的 MSVC 可用标准
- Win32 / COM
- `IExplorerCommand`
- Windows Implementation Library (WIL) 可使用，但不得引入重量级运行时
- Explorer DLL 禁止 C++/CLI

### Windows integration

- sparse package / packaging with external location
- `windows.comServer`
- `com:SurrogateServer`
- `windows.fileExplorerContextMenus`
- `Directory\Background`

## 推荐仓库结构

```text
ShellCommand11/
├── AGENTS.md
├── ShellCommand.sln
├── docs/
├── src/
│   ├── ShellCommand.Core/
│   ├── ShellCommand.Config.Yaml/
│   ├── ShellCommand.Broker/
│   ├── ShellCommand.App/
│   └── ShellCommand.Explorer/
├── packaging/
│   ├── manifest/
│   └── scripts/
└── tests/
    ├── ShellCommand.Core.Tests/
    ├── ShellCommand.Config.Tests/
    ├── ShellCommand.Broker.Tests/
    └── ShellCommand.Integration.Tests/
```

不要为了目录对称创建空项目。

## 构建目标

初期至少提供：

```text
packaging\scripts\Build.cmd
packaging\scripts\Test.cmd
packaging\scripts\Package.cmd
```

安装、卸载和 Explorer 重启由发布包中的 `ShellCommand.exe` 负责；开发者生成包后也通过该 App 验证安装流程。

## 实现顺序

AI 应按以下顺序推进，避免先碰 Explorer 黑盒再回头补核心：

### Phase 1 — Core

1. 建立解决方案和项目边界；
2. 实现 Core Model；
3. 实现 when 条件评价；
4. 单元测试 v2 条件、变量和选择上下文。

完成条件：不引用任何 Win32/COM/WPF/YamlDotNet。

### Phase 2 — YAML Adapter

1. 实现目录配置解析；
2. 实现全局配置解析；
3. 严格限制 YAML 子集与输入大小；
4. 产生行/列诊断；
5. 添加 v2 示例、错误输入与严格子集测试。

### Phase 3 — Broker

1. 配置发现；
2. Parsed cache + Last Known Good；
3. Directory fact probing；
4. ResolveMenu；
5. 二进制 Named Pipe Server；
6. action token；
7. 命令执行。

此时先做一个测试 client，不依赖 Explorer。

### Phase 4 — App

实现最小管理 UI：

- Broker status；
- config status；
- validate；
- preview；
- open config folder；
- diagnostics。

### Phase 5 — Explorer

1. Native COM DLL；
2. root command；
3. `EnumSubCommands`；
4. Named Pipe client；
5. timeout/fallback；
6. `Invoke`；
7. sparse package dev registration。

### Phase 6 — Context Menu Manager

1. discovery；
2. details；
3. reversible state journal；
4. static verb disable；
5. COM block/unblock；
6. packaged command 只读发现；
7. Explorer restart flow。

## 测试分类

### Unit

必须覆盖：

- when 三值逻辑 truth table；
- invalid YAML；
- unknown key；
- limits；
- global + local merge；
- separator normalization；
- variable expansion；
- command model validation。

### Broker integration

必须覆盖：

- pipe framing；
- malformed length；
- wrong protocol version；
- large payload rejection；
- concurrent resolve；
- config edit during request；
- Last Known Good；
- action token TTL；
- Broker restart。

### Windows integration

至少手工/自动混合验证：

- Windows 11 新式一级菜单出现；
- 不需要 “Show more options”；
- Unicode/空格/长路径目录；
- Broker kill 后右键仍快速；
- invalid YAML 不影响 Explorer；
- Explorer restart 后重新加载；
- install/uninstall 不残留 ShellCommand 注册。

## 性能测试

不要只测平均值。

记录：

```text
p50
p95
p99
max
```

重点场景：

- warm Broker + no local config；
- warm Broker + cached local config；
- first-seen local config；
- recently changed local config；
- Broker busy；
- Broker unavailable。

`patterns/ipc-and-time-budget.md` 定义 Explorer 的硬预算。

## 代码质量

- Nullable 开启；
- warnings as errors（第三方生成代码除外）；
- 不吞异常后继续产生不确定状态；
- 不在 Core 中使用静态全局服务定位器；
- 不让 App 直接写业务注册表；由 Broker/明确的 elevated action 完成；
- Native COM 方法必须 `noexcept` 风格处理边界，任何异常转换为安全 HRESULT/隐藏状态。

## 配置测试基线

不读取或迁移旧配置。测试 v2 的 all/any/not/exists、source-relative 路径、标量与列表变量、each、脚本文本不插值、disabled 覆盖、子菜单、include 原子发布、LKG、明确删除和不可读差异。打包后执行器测试空参数、中文、引号、尾部反斜杠、环境变量和并发排空输出。

## 不做的预优化

V11.0 不因为猜测性能问题就：

- 把 Broker 全部改成 C++；
- 自研 YAML parser；
- 自研通用 RPC 框架；
- 上共享内存；
- 提前拆独立 OneQuick Core 仓库。

先测量，再改变架构。
