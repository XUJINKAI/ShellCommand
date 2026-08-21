using System.Text;
using ShellCommand.Core;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ShellCommand.Config.Yaml;

public sealed record ParseResult<T>(T? Value, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && Value is not null;
}

public sealed class ConfigParser
{
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxScalarLength = 4096;
    public const int MaxCommands = 100;
    public const int MaxNesting = 8;

    public static ParseResult<DirectoryConfig> ParseDirectory(string text, string sourcePath = ".shellcommand.yaml")
    {
        var parsed = Load(text, sourcePath);
        if (parsed.Root is not YamlSequenceNode sequence)
            return Invalid<DirectoryConfig>(sourcePath, "INVALID_ROOT", "Directory configuration must be a YAML sequence.", parsed.Root);

        var diagnostics = new List<Diagnostic>(parsed.Diagnostics);
        var commands = ParseCommands(sequence, sourcePath, diagnostics);
        return new ParseResult<DirectoryConfig>(diagnostics.Any(IsError) ? null : new DirectoryConfig(commands), diagnostics);
    }

    public static ParseResult<GlobalConfig> ParseGlobal(string text, string sourcePath = "global.shellcommand.yaml")
    {
        var parsed = Load(text, sourcePath);
        if (parsed.Root is not YamlMappingNode mapping)
            return Invalid<GlobalConfig>(sourcePath, "INVALID_ROOT", "Global configuration must be a YAML mapping.", parsed.Root);

        var diagnostics = new List<Diagnostic>(parsed.Diagnostics);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "GlobalCommands", "Functions" };
        foreach (var key in Keys(mapping))
            if (key.Value is null || !allowed.Contains(key.Value)) Add(diagnostics, sourcePath, key, "UNKNOWN_FIELD", $"Unknown global field '{key.Value}'.");

        var commandsNode = Get(mapping, "GlobalCommands");
        var commands = new List<CommandDefinition>();
        if (commandsNode is not null)
        {
            if (commandsNode is not YamlSequenceNode commandSequence)
                Add(diagnostics, sourcePath, commandsNode, "INVALID_TYPE", "GlobalCommands must be a sequence.");
            else
                commands = ParseCommands(commandSequence, sourcePath, diagnostics);
        }

        var functions = ParseFunctions(Get(mapping, "Functions"), sourcePath, diagnostics);
        return new ParseResult<GlobalConfig>(diagnostics.Any(IsError) ? null : new GlobalConfig(commands, functions), diagnostics);
    }

    private static List<CommandDefinition> ParseCommands(YamlSequenceNode sequence, string sourcePath, List<Diagnostic> diagnostics)
    {
        var commands = new List<CommandDefinition>();
        if (sequence.Children.Count > MaxCommands)
            Add(diagnostics, sourcePath, sequence, "TOO_MANY_COMMANDS", $"A configuration may contain at most {MaxCommands} commands.");

        foreach (var node in sequence.Children.Take(MaxCommands + 1))
        {
            if (node is not YamlMappingNode map)
            {
                Add(diagnostics, sourcePath, node, "INVALID_TYPE", "Each command must be a mapping.");
                continue;
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal) { "Name", "Command", "Match", "RunAsAdmin", "Icon" };
            foreach (var key in Keys(map))
                if (key.Value is null || !allowed.Contains(key.Value)) Add(diagnostics, sourcePath, key, "UNKNOWN_FIELD", $"Unknown command field '{key.Value}'.");

            var name = ReadString(Get(map, "Name"), sourcePath, diagnostics, "Name");
            var command = ReadString(Get(map, "Command"), sourcePath, diagnostics, "Command");
            var matchText = ReadString(Get(map, "Match"), sourcePath, diagnostics, "Match");
            var icon = ReadString(Get(map, "Icon"), sourcePath, diagnostics, "Icon");
            var runAsAdmin = ReadBoolean(Get(map, "RunAsAdmin"), sourcePath, diagnostics, "RunAsAdmin");

            if (name is null && command is not null) name = command;
            if (name is null)
            {
                Add(diagnostics, sourcePath, map, "MISSING_COMMAND", "An action requires Command; a separator is Name: ---.");
                continue;
            }

            var separator = name == "---";
            if (separator && command is not null)
                Add(diagnostics, sourcePath, map, "INVALID_SEPARATOR", "A separator must not have Command.");
            else if (!separator && command is null)
                Add(diagnostics, sourcePath, map, "MISSING_COMMAND", "An action requires Command.");

            MatchExpression? match = null;
            if (matchText is not null && !MatchExpression.TryParse(matchText, out match, out var error))
                Add(diagnostics, sourcePath, Get(map, "Match") ?? map, "INVALID_MATCH", error!);

            if (name is not null && (separator || command is not null))
                commands.Add(new CommandDefinition(name, separator ? null : command, match, runAsAdmin, icon));
        }
        return commands;
    }

    private static GlobalFunctions ParseFunctions(YamlNode? node, string sourcePath, List<Diagnostic> diagnostics)
    {
        if (node is null) return new GlobalFunctions();
        if (node is not YamlMappingNode map)
        {
            Add(diagnostics, sourcePath, node, "INVALID_TYPE", "Functions must be a mapping.");
            return new GlobalFunctions();
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "CopyPath", "EditGlobal" };
        foreach (var key in Keys(map))
            if (key.Value is null || !allowed.Contains(key.Value)) Add(diagnostics, sourcePath, key, "UNKNOWN_FIELD", $"Unknown Functions field '{key.Value}'.");
        return new GlobalFunctions(
            ReadBoolean(Get(map, "CopyPath"), sourcePath, diagnostics, "CopyPath"),
            ReadBoolean(Get(map, "EditGlobal"), sourcePath, diagnostics, "EditGlobal"));
    }

    private static (YamlNode? Root, List<Diagnostic> Diagnostics) Load(string text, string sourcePath)
    {
        var diagnostics = new List<Diagnostic>();
        if (text is null) { Add(diagnostics, sourcePath, null, "YAML_SYNTAX", "Configuration cannot be null."); return (null, diagnostics); }
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
        {
            Add(diagnostics, sourcePath, null, "VALUE_TOO_LONG", $"Configuration exceeds {MaxFileBytes} bytes.");
            return (null, diagnostics);
        }
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count != 1)
            {
                Add(diagnostics, sourcePath, null, "YAML_SYNTAX", "Configuration must contain exactly one YAML document.");
                return (null, diagnostics);
            }
            var root = stream.Documents[0].RootNode;
            ValidateTree(root, sourcePath, diagnostics, 0);
            return (root, diagnostics);
        }
        catch (YamlException ex)
        {
            Add(diagnostics, sourcePath, null, "YAML_SYNTAX", ex.Message);
            return (null, diagnostics);
        }
    }

    private static void ValidateTree(YamlNode node, string sourcePath, List<Diagnostic> diagnostics, int depth)
    {
        if (depth > MaxNesting) Add(diagnostics, sourcePath, node, "VALUE_TOO_LONG", $"YAML nesting may not exceed {MaxNesting}.");
        if (node.NodeType == YamlNodeType.Alias || !string.IsNullOrEmpty(node.Anchor))
            Add(diagnostics, sourcePath, node, "UNSUPPORTED_YAML_FEATURE", "Anchors and aliases are not supported.");
        if (!string.IsNullOrEmpty(node.Tag) && node.Tag != "?")
            Add(diagnostics, sourcePath, node, "UNSUPPORTED_YAML_FEATURE", "Explicit YAML tags are not supported.");
        if (node is YamlScalarNode scalar && (scalar.Value?.Length ?? 0) > MaxScalarLength)
            Add(diagnostics, sourcePath, node, "VALUE_TOO_LONG", $"Scalar values may not exceed {MaxScalarLength} characters.");
        if (node is YamlSequenceNode sequence)
            foreach (var child in sequence.Children) ValidateTree(child, sourcePath, diagnostics, depth + 1);
        else if (node is YamlMappingNode mapping)
            foreach (var pair in mapping.Children)
            {
                ValidateTree(pair.Key, sourcePath, diagnostics, depth + 1);
                ValidateTree(pair.Value, sourcePath, diagnostics, depth + 1);
            }
    }

    private static IEnumerable<YamlScalarNode> Keys(YamlMappingNode map)
        => map.Children.Keys.OfType<YamlScalarNode>();

    private static YamlNode? Get(YamlMappingNode map, string key)
        => map.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode scalar && scalar.Value == key).Value;

    private static string? ReadString(YamlNode? node, string sourcePath, List<Diagnostic> diagnostics, string field)
    {
        if (node is null) return null;
        if (node is not YamlScalarNode scalar)
        {
            Add(diagnostics, sourcePath, node, "INVALID_TYPE", $"{field} must be a string.");
            return null;
        }
        if (scalar.Value is null)
        {
            Add(diagnostics, sourcePath, node, "INVALID_TYPE", $"{field} must be a string.");
            return null;
        }
        return scalar.Value;
    }

    private static bool ReadBoolean(YamlNode? node, string sourcePath, List<Diagnostic> diagnostics, string field)
    {
        if (node is null) return false;
        if (node is YamlScalarNode scalar && scalar.Value is not null && bool.TryParse(scalar.Value, out var value)) return value;
        Add(diagnostics, sourcePath, node, "INVALID_TYPE", $"{field} must be a boolean.");
        return false;
    }

    private static ParseResult<T> Invalid<T>(string sourcePath, string code, string message, YamlNode? node)
        => new(default, new[] { new Diagnostic(sourcePath, DiagnosticSeverity.Error, code, message, Line(node), Column(node)) });

    private static bool IsError(Diagnostic diagnostic) => diagnostic.Severity == DiagnosticSeverity.Error;
    private static void Add(List<Diagnostic> diagnostics, string sourcePath, YamlNode? node, string code, string message)
        => diagnostics.Add(new Diagnostic(sourcePath, DiagnosticSeverity.Error, code, message, Line(node), Column(node)));
    private static int? Line(YamlNode? node) => node is null ? null : Math.Max(1, node.Start.Line);
    private static int? Column(YamlNode? node) => node is null ? null : node.Start.Column + 1;
}
