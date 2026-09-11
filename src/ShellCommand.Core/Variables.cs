using System.Text;
namespace ShellCommand.Core;

public static class VariableExpander
{
    public static IReadOnlyList<string> Names(string text)
    {
        var names = new List<string>();
        Transform(text, name => { names.Add(name); return ""; });
        return names;
    }
    public static string Expand(string text, MenuContext context, string sourcePath, ResolveEnvironment environment, SelectionItem? item = null)
        => Transform(text, name => name switch
        {
            "directory" => context.Directory ?? throw new InvalidOperationException("当前上下文没有确定的目录。"),
            "config_dir" => Path.GetDirectoryName(sourcePath)!,
            "app_dir" => environment.AppDirectory,
            "data_dir" => environment.DataDirectory,
            "item.path" => item?.Path ?? throw new InvalidOperationException("item 变量只能用于 each。"),
            "item.parent" => item is null ? throw new InvalidOperationException("item 变量只能用于 each。") : Path.GetDirectoryName(item.Path)!,
            "selection.paths" => throw new InvalidOperationException("路径列表必须占据完整数组元素或 copy.values。"),
            _ when name.StartsWith("env:", StringComparison.Ordinal) => environment.Variables.TryGetValue(name[4..], out var value)
                ? value : throw new InvalidOperationException("环境变量不存在：" + name[4..]),
            _ => throw new InvalidOperationException("未知变量：" + name)
        });
    public static IReadOnlyList<string> Arguments(IReadOnlyList<string> args, MenuContext context, string source,
        ResolveEnvironment environment, SelectionItem? item)
    {
        var result = new List<string>();
        foreach (var argument in args)
        {
            if (argument == "${selection.paths}")
            {
                if (!context.IsSelection) throw new InvalidOperationException("没有选择项。");
                result.AddRange(context.Selection.Select(s => s.Path));
            }
            else result.Add(Expand(argument, context, source, environment, item));
        }
        return result;
    }
    private static string Transform(string text, Func<string, string> replace)
    {
        var output = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            if (text.AsSpan(i).StartsWith("$${", StringComparison.Ordinal)) { output.Append("${"); i += 3; continue; }
            if (!text.AsSpan(i).StartsWith("${", StringComparison.Ordinal)) { output.Append(text[i++]); continue; }
            var end = text.IndexOf('}', i + 2);
            if (end < 0) throw new InvalidOperationException("变量缺少结束的 }。");
            output.Append(replace(text[(i + 2)..end])); i = end + 1;
            if (output.Length > 32768) throw new InvalidOperationException("变量展开结果过长。");
        }
        return output.ToString();
    }
}
