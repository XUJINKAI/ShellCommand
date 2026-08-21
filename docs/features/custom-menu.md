# Feature: Custom Menu

## Responsibility

根据当前目录、目录配置、全局配置和内置动作产生 ShellCommand 的一层动态子菜单。

## Owns

- Global/Directory command merge semantics；
- Match result 到可见菜单项的转换；
- separator normalization；
- built-in action placement；
- Resolved Menu Model。

## Does Not Own

- YAML syntax；
- Named Pipe；
- COM；
- Windows package registration；
- process creation；
- third-party context menu scanning；
- WPF layout。

## Inputs

```text
GlobalConfig?
DirectoryConfig?
DirectoryFacts
BuiltInCapabilities
```

## Output

```text
type ResolvedMenu = {
    items: ResolvedItem[]
}

type ResolvedItem =
    Action(title, icon, actionSpec)
  | Separator
```

这是概念模型，不规定 C# 具体类型。

## Resolution Rules

1. 评价 directory commands；
2. 删除未 Match 的命令；
3. 评价 global commands；
4. 删除未 Match 的命令；
5. directory/global 两组都非空时插入 separator；
6. 加入启用的 built-in functions；
7. 加入 `Open ShellCommand 11`；
8. normalize separators。

## Match Facts

Core 不应自己枚举磁盘目录。

Broker 提供评价 Match 所需的存在性能力/事实。实现可以使用 lazy probe，避免先枚举全部文件。

Match 必须遵守 `contracts/config.md`，不能悄悄扩成任意表达式语言。

## Invariants

- 相同配置 + 相同目录事实 → 相同菜单顺序；
- Match 查询不产生系统副作用；
- Match false 的项不出现在菜单；
- 最终菜单没有首尾 separator；
- 最终菜单没有连续 separator；
- `Open ShellCommand 11` 不依赖用户 YAML 才存在；
- 单次菜单 item 数不超过 IPC Contract 限制。

## Errors

单个 icon 解析失败不能使整个菜单失败。

单个 command 的 schema/semantic error 会使所属 config source invalid；不会尝试“半解析半使用”同一个新版本文件。

## Acceptance

给旧模板中的典型配置时，应得到符合旧意图、但遵循 V11 隐藏语义的菜单。
