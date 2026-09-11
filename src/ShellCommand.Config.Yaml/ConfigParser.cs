using System.Text;
using ShellCommand.Core;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
namespace ShellCommand.Config.Yaml;

public sealed record ParseResult<T>(T? Value, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Value is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
public sealed class ConfigParser
{
    public const int MaxFileBytes = 256 * 1024;
    private readonly string _source;
    private int _nodes;
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private ConfigParser(string source) => _source = source;
    private sealed class InvalidConfig : Exception
    {
        public YamlNode? Node { get; }
        public string Field { get; }
        public InvalidConfig(YamlNode? node, string field, string message) : base(message) { Node = node; Field = field; }
    }
    public static ParseResult<MenuConfig> Parse(string text, string sourcePath)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new InvalidConfig(null, "", "文件超过 256 KiB。");
            Preflight(text);
            var yaml = new YamlStream(); yaml.Load(new StringReader(text));
            if (yaml.Documents.Count != 1) throw new InvalidConfig(null, "", "需要且只能有一个 YAML 文档。");
            var parser = new ConfigParser(Path.GetFullPath(sourcePath));
            var root = Map(yaml.Documents[0].RootNode, "root", "version", "include", "menu");
            if (ScalarValue(Get(root, "version"), "version") != "2") throw new InvalidConfig(root, "version", "只支持 version: 2。");
            var include = Get(root, "include");
            var includes = include is not null ? Strings(include, "include") : [];
            foreach (var path in includes)
                if (path.Length == 0 || path.Contains('*') || path.Contains('?') || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal))
                    throw new InvalidConfig(include, "include", "include 只能引用固定的本地文件路径。");
            var menu = parser.Menu(Get(root, "menu"), "menu", 0);
            return new(new(menu, includes), []);
        }
        catch (InvalidConfig ex) { return new(null, [new(sourcePath, DiagnosticSeverity.Error, "INVALID_CONFIG", ex.Message, ex.Node?.Start.Line, ex.Node?.Start.Column + 1, ex.Field)]); }
        catch (YamlException ex) { return new(null, [new(sourcePath, DiagnosticSeverity.Error, "YAML_SYNTAX", ex.Message, ex.Start.Line, ex.Start.Column + 1)]); }
        catch (InvalidOperationException ex) { return new(null, [new(sourcePath, DiagnosticSeverity.Error, "INVALID_VARIABLE", ex.Message)]); }
    }
    private static void Preflight(string text)
    {
        var parser = new Parser(new StringReader(text));
        var depth = 0; var events = 0;
        while (parser.MoveNext())
        {
            var current = parser.Current!;
            if (++events > 8192) throw new YamlException(current.Start, current.End, "YAML 节点过多。");
            if (current is AnchorAlias) throw new YamlException(current.Start, current.End, "不支持 YAML 别名。");
            if (current is NodeEvent node && (!string.IsNullOrEmpty(node.Anchor) || !string.IsNullOrEmpty(node.Tag)))
                throw new YamlException(current.Start, current.End, "不支持锚点或显式标签。");
            if (current is MappingStart or SequenceStart && ++depth > 16) throw new YamlException(current.Start, current.End, "YAML 深度超过 16。");
            if (current is MappingEnd or SequenceEnd) depth--;
            if (current is Scalar scalar && scalar.Value.Length > 32768) throw new YamlException(current.Start, current.End, "单值超过 32768 字符。");
        }
    }
    private List<MenuDefinition> Menu(YamlNode? node, string field, int depth)
    {
        if (depth > 2) throw new InvalidConfig(node, field, "最多两层自定义分组。");
        var sequence = Seq(node, field);
        var result = new List<MenuDefinition>();
        foreach (var child in sequence.Children)
        {
            if (++_nodes > 100) throw new InvalidConfig(child, field, "菜单超过 100 个节点。");
            var map = Map(child, field, "id", "title", "enabled", "separator", "when", "icon", "run", "script", "open", "copy", "items");
            if (Get(map, "separator") is { } separator)
            {
                if (!Bool(separator, field) || map.Children.Count != 1) throw new InvalidConfig(child, field, "分隔线只允许 separator: true。");
                result.Add(new("", "", _source, Separator: true)); continue;
            }
            var id = Str(Get(map, "id"), field + ".id");
            if (id.Length is < 1 or > 64 || !_ids.Add(id)) throw new InvalidConfig(child, field + ".id", "ID 不能为空、重复或超过 64 字符。");
            var f = "menu." + id;
            var enabled = Get(map, "enabled") is not { } enabledNode || Bool(enabledNode, f + ".enabled");
            if (!enabled && map.Children.Count == 2) { result.Add(new(id, id, _source, false)); continue; }
            var title = Str(Get(map, "title"), f + ".title");
            if (title.Length is < 1 or > 256) throw new InvalidConfig(child, f, "标题长度需要 1–256 字符。");
            var actionFields = new[] { "run", "script", "open", "copy", "items" };
            if (actionFields.Count(k => Get(map, k) is not null) != 1) throw new InvalidConfig(child, f, "需要且只能有一个 run/script/open/copy/items。");
            var whenNodes = 0;
            var when = Get(map, "when") is { } whenNode ? When(whenNode, f + ".when", 0, ref whenNodes) : null;
            var icon = Get(map, "icon") is { } iconNode ? Icon(iconNode, f + ".icon") : null;
            var run = Get(map, "run") is { } runNode ? Run(runNode, f + ".run") : null;
            var script = Get(map, "script") is { } scriptNode ? Script(scriptNode, f + ".script") : null;
            var open = Get(map, "open") is { } openNode ? Template(openNode, f + ".open") : null;
            var copy = Get(map, "copy") is { } copyNode ? Copy(copyNode, f + ".copy") : null;
            var items = Get(map, "items") is { } itemsNode ? Menu(itemsNode, f + ".items", depth + 1) : null;
            result.Add(new(id, title, _source, enabled, When: when, Icon: icon, Run: run, Script: script, Open: open, Copy: copy, Items: items));
        }
        return result;
    }
    private static RunDefinition Run(YamlNode node, string f)
    {
        var map = Map(node, f, "exe", "args", "cwd", "env", "admin", "output", "mode");
        var mode = Choice(Get(map, "mode"), f + ".mode", "once", "once", "each");
        var each = mode == "each";
        var exe = Template(Get(map, "exe"), f + ".exe", each: each);
        var args = Get(map, "args") is { } argsNode ? Seq(argsNode, f + ".args").Children.Select(n => Template(n, f + ".args", true, each)).ToArray() : [];
        if (args.Length > 256) throw new InvalidConfig(node, f, "args 最多 256 个元素。");
        var cwd = Get(map, "cwd") is { } cwdNode ? Template(cwdNode, f + ".cwd", each: each) : null;
        var env = EnvironmentMap(Get(map, "env"), f + ".env", each);
        var admin = Get(map, "admin") is { } adminNode && Bool(adminNode, f + ".admin");
        var output = Choice(Get(map, "output"), f + ".output", "normal", "normal", "hidden", "window");
        if (admin && (output != "normal" || env is not null)) throw new InvalidConfig(node, f, "admin 只支持 normal 输出，不能设置 env。");
        return new(exe, args, cwd, env, admin, output, mode);
    }
    private static ScriptDefinition Script(YamlNode node, string f)
    {
        var map = Map(node, f, "shell", "text", "cwd", "env", "output");
        var shell = Choice(Get(map, "shell"), f + ".shell", "powershell", "powershell", "pwsh", "cmd");
        var text = Str(Get(map, "text"), f + ".text");
        var cwd = Get(map, "cwd") is { } cwdNode ? Template(cwdNode, f + ".cwd") : null;
        var env = EnvironmentMap(Get(map, "env"), f + ".env", false);
        return new(shell, text, cwd, env, Choice(Get(map, "output"), f + ".output", "normal", "normal", "hidden", "window"));
    }
    private static Dictionary<string, string>? EnvironmentMap(YamlNode? node, string f, bool each)
    {
        if (node is null) return null;
        var map = Map(node, f);
        if (map.Children.Count > 64) throw new InvalidConfig(node, f, "env 最多 64 项。");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in map.Children)
        {
            var key = Str(pair.Key, f);
            if (key.Length == 0 || key.Contains('=') || key.Contains('\0') || !result.TryAdd(key, Template(pair.Value, f + "." + key, each: each)))
                throw new InvalidConfig(pair.Key, f, "环境变量名称非法或重复。");
        }
        return result;
    }
    private static CopyDefinition Copy(YamlNode node, string f)
    {
        if (node is YamlScalarNode) return new(Text: Template(node, f));
        var map = Map(node, f, "values", "separator");
        if (Str(Get(map, "values"), f) != "${selection.paths}") throw new InvalidConfig(node, f, "copy.values 必须为 ${selection.paths}。");
        return new(Values: "${selection.paths}", Separator: Get(map, "separator") is { } separator ? Str(separator, f + ".separator") : "\r\n");
    }
    private static IconDefinition Icon(YamlNode node, string f)
    {
        if (node is YamlScalarNode) return new(Builtin: Choice(node, f, "terminal", "terminal", "folder", "file", "copy", "settings", "code", "git"));
        var map = Map(node, f, "file", "index");
        return new(File: Template(Get(map, "file"), f + ".file"), Index: Get(map, "index") is { } index ? Integer(index, f + ".index", 0, 65535) : 0);
    }
    private static Condition When(YamlNode node, string f, int depth, ref int nodes)
    {
        if (depth > 8 || ++nodes > 64) throw new InvalidConfig(node, f, "条件过于复杂（最多 8 层、64 节点）。");
        var map = Map(node, f, "all", "any", "not", "exists", "context", "selection");
        if (map.Children.Count != 1) throw new InvalidConfig(node, f, "一个条件只能包含一个运算符。");
        var pair = map.Children.Single(); var op = Str(pair.Key, f); var value = pair.Value;
        if (op is "all" or "any")
        {
            var children = new List<Condition>();
            foreach (var child in Seq(value, f).Children) children.Add(When(child, f + "." + op, depth + 1, ref nodes));
            if (children.Count == 0) throw new InvalidConfig(node, f, "条件数组不能为空。");
            return new(op, Children: children);
        }
        if (op == "not") return new(op, Children: [When(value, f + ".not", depth + 1, ref nodes)]);
        if (op == "context") return new(op, Choice(value, f, "background", "background", "selection"));
        if (op == "exists")
        {
            var pattern = Str(value, f);
            if (pattern.Length is < 1 or > 256 || pattern.Contains('/') || pattern.Contains('\\') || pattern.Contains("..", StringComparison.Ordinal) || pattern.Contains(':'))
                throw new InvalidConfig(value, f, "exists 只支持直接子项名称及 * ?。");
            return new(op, pattern);
        }
        var selection = Map(value, f, "types", "count", "extensions");
        var types = Get(selection, "types") is { } typesNode ? Strings(typesNode, f + ".types") : null;
        if (types is not null && (types.Length == 0 || types.Any(t => t is not ("file" or "folder")))) throw new InvalidConfig(value, f, "types 只能包含 file/folder。");
        var min = 1; var max = 256;
        if (Get(selection, "count") is { } count)
        {
            var range = Map(count, f + ".count", "min", "max");
            if (Get(range, "min") is { } lo) min = Integer(lo, f, 1, 256);
            if (Get(range, "max") is { } hi) max = Integer(hi, f, min, 256);
        }
        var extensions = Get(selection, "extensions") is { } extensionsNode ? Strings(extensionsNode, f + ".extensions") : null;
        if (extensions is not null && (extensions.Length == 0 || extensions.Any(e => !e.StartsWith('.') || e.Contains('/') || e.Contains('\\')))) throw new InvalidConfig(value, f, "扩展名需要以 . 开头。");
        return new(op, Selection: new(types, min, max, extensions));
    }
    private static string Template(YamlNode? node, string f, bool list = false, bool each = false)
    {
        var text = Str(node, f);
        foreach (var name in VariableExpander.Names(text))
        {
            var known = name is "directory" or "config_dir" or "app_dir" or "data_dir" || name.StartsWith("env:", StringComparison.Ordinal) && name.Length > 4;
            if (name == "selection.paths") known = list && text == "${selection.paths}";
            if (name is "item.path" or "item.parent") known = each;
            if (!known) throw new InvalidConfig(node, f, "未知或不适用于此字段的变量：" + name);
        }
        return text;
    }
    private static YamlMappingNode Map(YamlNode? node, string f, params string[] allowed)
    {
        if (node is not YamlMappingNode map) throw new InvalidConfig(node, f, "需要映射对象。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in map.Children)
        {
            var key = Str(pair.Key, f);
            if (key == "<<" || !keys.Add(key) || allowed.Length != 0 && !allowed.Contains(key, StringComparer.Ordinal))
                throw new InvalidConfig(pair.Key, f, "未知、重复或不支持的字段：" + key);
        }
        return map;
    }
    private static YamlNode? Get(YamlMappingNode map, string key) => map.Children.FirstOrDefault(p => p.Key is YamlScalarNode s && s.Value == key).Value;
    private static YamlSequenceNode Seq(YamlNode? node, string f) => node as YamlSequenceNode ?? throw new InvalidConfig(node, f, "需要数组。");
    private static string ScalarValue(YamlNode? node, string f) => node is YamlScalarNode { Value: { } text } && !text.Contains('\0') ? text : throw new InvalidConfig(node, f, "需要字符串值。");
    private static string Str(YamlNode? node, string f)
    {
        var text = ScalarValue(node, f);
        if (node is YamlScalarNode { Style: ScalarStyle.Plain } &&
            (text is "true" or "false" or "null" or "~" || double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
            throw new InvalidConfig(node, f, "需要字符串；数字或布尔值作为文本时请加引号。");
        return text;
    }
    private static string[] Strings(YamlNode node, string f) => Seq(node, f).Children.Select(n => Str(n, f)).ToArray();
    private static bool Bool(YamlNode node, string f) => ScalarValue(node, f) switch { "true" => true, "false" => false, _ => throw new InvalidConfig(node, f, "需要 true 或 false。") };
    private static int Integer(YamlNode node, string f, int min, int max) => int.TryParse(ScalarValue(node, f), out var value) && value >= min && value <= max ? value : throw new InvalidConfig(node, f, $"需要 {min}–{max} 的整数。");
    private static string Choice(YamlNode? node, string f, string fallback, params string[] options)
    {
        var value = node is null ? fallback : Str(node, f);
        return options.Contains(value, StringComparer.Ordinal) ? value : throw new InvalidConfig(node, f, "只支持 " + string.Join("/", options));
    }
}
