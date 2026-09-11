using System.Text.Json;
namespace ShellCommand.Core;

public static class MenuResolver
{
    public static ResolvedMenu Resolve(MenuConfig? global, MenuConfig? local, IReadOnlyList<string>? facts,
        MenuContext context, ResolveEnvironment environment)
    {
        var diagnostics = new List<Diagnostic>();
        IEnumerable<MenuDefinition> Flatten(IEnumerable<MenuDefinition> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Items ?? [])));
        var overrides = Flatten(local?.Menu ?? []).Where(m => !m.Separator).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        IEnumerable<MenuDefinition> Filter(IEnumerable<MenuDefinition> nodes) => nodes.Where(n => !overrides.Contains(n.Id)).Select(n => n with { Items = n.Items is null ? null : Filter(n.Items).ToArray() });
        var items = new List<ResolvedItem>();
        items.AddRange(ResolveItems(local?.Menu ?? [], facts, context, environment, diagnostics));
        if (items.Count != 0) items.Add(new("separator"));
        items.AddRange(ResolveItems(Filter(global?.Menu ?? []), facts, context, environment, diagnostics));
        if (Count(items) > 100)
        {
            diagnostics.Add(new("", DiagnosticSeverity.Error, "MENU_LIMIT", "合并菜单超过 100 项，请减少命令。"));
            items.Clear();
        }
        if (items.Count != 0) items.Add(new("separator"));
        items.Add(new("action", "设置…", new("设置", "", [new("settings")])));
        return new(Normalize(items), diagnostics);
    }
    private static int Count(IEnumerable<ResolvedItem> items) => items.Sum(i => 1 + Count(i.Items ?? []));
    private static IReadOnlyList<ResolvedItem> ResolveItems(IEnumerable<MenuDefinition> definitions, IReadOnlyList<string>? facts,
        MenuContext context, ResolveEnvironment environment, List<Diagnostic> diagnostics, bool inheritedContext = false)
    {
        var result = new List<ResolvedItem>();
        foreach (var node in definitions)
        {
            if (!node.Enabled) continue;
            if (node.Separator) { result.Add(new("separator")); continue; }
            try
            {
                var hasContext = inheritedContext || Conditions.Contains(node.When, "context");
                var selectionDefault = Conditions.Contains(node.When, "selection");
                if (node.Items is null && !hasContext && context.IsSelection != selectionDefault)
                    throw new InvalidOperationException("不适用于当前背景/选择上下文。");
                if (node.When is not null && Conditions.Evaluate(node.When, context, facts) != Truth.True)
                    throw new InvalidOperationException("条件未满足，或目录事实尚未准备完成。");
                if (node.Items is not null)
                {
                    var children = ResolveItems(node.Items, facts, context, environment, diagnostics, hasContext || selectionDefault);
                    if (children.Count != 0) result.Add(new("group", node.Title, IconRef: Icon(node), Items: children));
                }
                else result.Add(new("action", node.Title, BuildPlan(node, context, environment), Icon(node)));
            }
            catch (InvalidOperationException ex) { diagnostics.Add(new(node.SourcePath, DiagnosticSeverity.Info, "HIDDEN", node.Title + "：" + ex.Message, Field: node.Id)); }
        }
        return Normalize(result);
    }
    private static string Icon(MenuDefinition node) => node.Icon?.File ?? node.Icon?.Builtin ?? "";
    public static LaunchPlan BuildPlan(MenuDefinition node, MenuContext context, ResolveEnvironment environment)
    {
        string Expand(string value, SelectionItem? item = null) => VariableExpander.Expand(value, context, node.SourcePath, environment, item);
        string Relative(string value) => Path.IsPathFullyQualified(value) ? value : Path.GetFullPath(value, Path.GetDirectoryName(node.SourcePath)!);
        IReadOnlyDictionary<string, string>? Env(IReadOnlyDictionary<string, string>? values, SelectionItem? item = null)
            => values?.ToDictionary(pair => pair.Key, pair => Expand(pair.Value, item), StringComparer.OrdinalIgnoreCase);
        if (node.Run is { } run)
        {
            if (run.Mode == "each" && !context.IsSelection) throw new InvalidOperationException("each 需要选择项。");
            var items = run.Mode == "each" ? context.Selection.Cast<SelectionItem?>() : new SelectionItem?[] { null };
            var actions = new List<LaunchAction>();
            foreach (var item in items)
            {
                var exe = Expand(run.Exe, item);
                if (!Path.IsPathFullyQualified(exe) && (exe.Contains('/') || exe.Contains('\\'))) exe = Relative(exe);
                var cwd = run.Cwd is null ? context.Directory : Relative(Expand(run.Cwd, item));
                if (cwd is null) throw new InvalidOperationException("缺少目录，请显式设置 cwd。跨目录选择可使用 ${item.parent}。" );
                actions.Add(new("run", exe, VariableExpander.Arguments(run.Args, context, node.SourcePath, environment, item), cwd, Env(run.Env, item), run.Admin, run.Output));
            }
            return new(node.Title, node.SourcePath, actions);
        }
        if (node.Script is { } script)
        {
            var cwd = script.Cwd is null ? context.Directory : Relative(Expand(script.Cwd));
            if (cwd is null) throw new InvalidOperationException("脚本缺少 cwd。");
            var env = new Dictionary<string, string>(Env(script.Env) ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
            {
                ["SC_DIRECTORY"] = context.Directory ?? "",
                ["SC_CONFIG_DIR"] = Path.GetDirectoryName(node.SourcePath)!,
                ["SC_SELECTION_JSON"] = JsonSerializer.Serialize(context.Selection.Select(i => i.Path))
            };
            return new(node.Title, node.SourcePath, [new("script", Cwd: cwd, Env: env, Output: script.Output, Text: script.Text, Shell: script.Shell)]);
        }
        if (node.Open is { } open)
        {
            var target = Expand(open);
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.IsFile) target = Relative(target);
            return new(node.Title, node.SourcePath, [new("open", Text: target)]);
        }
        if (node.Copy is { } copy)
        {
            if (copy.Values is not null && !context.IsSelection) throw new InvalidOperationException("没有选择项。");
            var value = copy.Text is not null ? Expand(copy.Text) : string.Join(copy.Separator, context.Selection.Select(i => i.Path));
            return new(node.Title, node.SourcePath, [new("copy", Text: value)]);
        }
        throw new InvalidOperationException("没有可执行动作。");
    }
    private static IReadOnlyList<ResolvedItem> Normalize(IEnumerable<ResolvedItem> items)
    {
        var result = new List<ResolvedItem>();
        foreach (var item in items)
            if (item.Kind != "separator" || result.Count != 0 && result[^1].Kind != "separator") result.Add(item);
        if (result.Count != 0 && result[^1].Kind == "separator") result.RemoveAt(result.Count - 1);
        return result;
    }
}
