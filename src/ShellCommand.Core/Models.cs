namespace ShellCommand.Core;

public enum DiagnosticSeverity { Info, Warning, Error }
public sealed record Diagnostic(string SourcePath, DiagnosticSeverity Severity, string Code, string Message,
    int? Line = null, int? Column = null, string? Field = null);
public enum Truth { Unknown, False, True }
public sealed record SelectionItem(string Path, bool IsFolder);
public sealed record MenuContext(string? Directory, IReadOnlyList<SelectionItem> Selection)
{
    public bool IsSelection => Selection.Count != 0;
}
public sealed record SelectionRule(IReadOnlyList<string>? Types = null, int Min = 1, int Max = 256,
    IReadOnlyList<string>? Extensions = null);
public sealed record Condition(string Operator, string? Value = null, IReadOnlyList<Condition>? Children = null,
    SelectionRule? Selection = null);
public sealed record IconDefinition(string? Builtin = null, string? File = null, int Index = 0);
public sealed record RunDefinition(string Exe, IReadOnlyList<string> Args, string? Cwd = null,
    IReadOnlyDictionary<string, string>? Env = null, bool Admin = false, string Output = "normal", string Mode = "once");
public sealed record ScriptDefinition(string Shell, string Text, string? Cwd = null,
    IReadOnlyDictionary<string, string>? Env = null, string Output = "normal");
public sealed record CopyDefinition(string? Text = null, string? Values = null, string Separator = "\r\n");
public sealed record MenuDefinition(string Id, string Title, string SourcePath, bool Enabled = true,
    bool Separator = false, Condition? When = null, IconDefinition? Icon = null,
    RunDefinition? Run = null, ScriptDefinition? Script = null, string? Open = null, CopyDefinition? Copy = null,
    IReadOnlyList<MenuDefinition>? Items = null);
public sealed record MenuConfig(IReadOnlyList<MenuDefinition> Menu, IReadOnlyList<string> Includes);
public sealed record SourceSnapshot(MenuConfig? Config, IReadOnlyList<Diagnostic> Diagnostics, bool Missing = false);
public sealed record PreparedSnapshot(SourceSnapshot Global, SourceSnapshot Local, IReadOnlyList<string>? Facts,
    IReadOnlyList<string> Dependencies, DateTimeOffset PreparedAt);
public sealed record LaunchAction(string Kind, string? Exe = null, IReadOnlyList<string>? Args = null,
    string? Cwd = null, IReadOnlyDictionary<string, string>? Env = null, bool Admin = false,
    string Output = "normal", string? Text = null, string? Shell = null);
public sealed record LaunchPlan(string Title, string SourcePath, IReadOnlyList<LaunchAction> Actions);
public sealed record ResolvedItem(string Kind, string Title = "", LaunchPlan? Plan = null, string IconRef = "",
    IReadOnlyList<ResolvedItem>? Items = null);
public sealed record ResolvedMenu(IReadOnlyList<ResolvedItem> Items, IReadOnlyList<Diagnostic> Diagnostics);
public sealed record ResolveEnvironment(string AppDirectory, string DataDirectory, IReadOnlyDictionary<string, string> Variables);
