namespace ShellCommand.Core;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(
    string SourcePath,
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    int? Line = null,
    int? Column = null);

public sealed record CommandDefinition(
    string Name,
    string? Command,
    MatchExpression? Match = null,
    bool RunAsAdmin = false,
    string? Icon = null)
{
    public bool IsSeparator => Command is null && Name == "---";
}

public sealed record DirectoryConfig(IReadOnlyList<CommandDefinition> Commands);

public sealed record GlobalFunctions(bool CopyPath = false, bool EditGlobal = false);

public sealed record GlobalConfig(
    IReadOnlyList<CommandDefinition> Commands,
    GlobalFunctions Functions);

public interface IDirectoryFacts
{
    bool Exists(string leafName);
}

public sealed class SetDirectoryFacts : IDirectoryFacts
{
    private readonly HashSet<string> _entries;

    public SetDirectoryFacts(IEnumerable<string> entries)
        => _entries = new(entries, StringComparer.OrdinalIgnoreCase);

    public bool Exists(string leafName)
        => leafName.Contains('*') || leafName.Contains('?')
            ? _entries.Any(value => MatchExpression.WildcardMatch(leafName, value))
            : _entries.Contains(leafName);
}

public enum ActionKind
{
    UserCommand,
    CopyPath,
    EditGlobal,
    OpenApp
}

public sealed record ActionSpec(
    ActionKind Kind,
    string? Command = null,
    string? WorkingDirectory = null,
    bool RunAsAdmin = false,
    string? Icon = null);

public abstract record ResolvedItem
{
    private ResolvedItem() { }

    public sealed record Action(
        string Title,
        ActionSpec Spec,
        string? IconRef = null,
        string Source = "user") : ResolvedItem;

    public sealed record Separator : ResolvedItem;
}

public sealed record ResolvedMenu(IReadOnlyList<ResolvedItem> Items);

public sealed record BuiltInCapabilities(
    bool CopyPath,
    bool EditGlobal,
    bool OpenApp = true);

public sealed record ConfigSnapshot(
    GlobalConfig? Global,
    DirectoryConfig? Directory,
    IReadOnlyList<Diagnostic> Diagnostics);
