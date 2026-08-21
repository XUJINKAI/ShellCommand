# Decision 0003: YAML Is an Adapter, Not Core

## Decision

`ShellCommand.Core` 不引用 YamlDotNet，也不暴露 YAML-specific 类型。

YAML 解析放在独立 `ShellCommand.Config.Yaml`。

## Reason

配置文件是 serialization/input format，不是产品核心规则。

分离后：

```text
YAML ─┐
GUI  ─┼→ Core Model → Resolve
Test ─┘
```

这让 Core：

- 容易测试；
- 可被其他宿主复用；
- 不把 Parser crash/复杂性传播进 Explorer；
- 为未来 OneQuick Core 实验提供真实边界。

## Rejected

### Core model decorated with YAML attributes everywhere

会让配置格式侵入业务模型，并让未来替换 serialization 变难。

### Explorer parses YAML independently

会产生两套 parser/semantics，并破坏单一规则来源。

## Reconsider When

不需要。即使未来只剩 YAML，这个依赖方向仍然合理。
