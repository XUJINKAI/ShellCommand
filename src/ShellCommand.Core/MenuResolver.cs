namespace ShellCommand.Core;

public static class MenuResolver
{
    public const string OpenAppTitle = "Open ShellCommand 11";

    public static ResolvedMenu Resolve(
        GlobalConfig? global,
        DirectoryConfig? directory,
        IDirectoryFacts facts,
        string workingDirectory,
        BuiltInCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        capabilities ??= new BuiltInCapabilities(global?.Functions.CopyPath ?? false, global?.Functions.EditGlobal ?? false);

        var items = new List<ResolvedItem>();
        var local = ResolveCommands(directory?.Commands, facts, workingDirectory, "directory");
        var globalItems = ResolveCommands(global?.Commands, facts, workingDirectory, "global");
        items.AddRange(local);
        if (local.Count > 0 && globalItems.Count > 0) items.Add(new ResolvedItem.Separator());
        items.AddRange(globalItems);
        if (capabilities.CopyPath) items.Add(new ResolvedItem.Action("Copy Folder Path", new(ActionKind.CopyPath, WorkingDirectory: workingDirectory), Source: "built-in"));
        if (capabilities.EditGlobal) items.Add(new ResolvedItem.Action("Edit Global Config", new(ActionKind.EditGlobal, WorkingDirectory: workingDirectory), Source: "built-in"));
        if (capabilities.OpenApp) items.Add(new ResolvedItem.Action(OpenAppTitle, new(ActionKind.OpenApp), Source: "built-in"));

        return new ResolvedMenu(NormalizeSeparators(items));
    }

    private static List<ResolvedItem.Action> ResolveCommands(
        IReadOnlyList<CommandDefinition>? definitions,
        IDirectoryFacts facts,
        string workingDirectory,
        string source)
    {
        var result = new List<ResolvedItem.Action>();
        if (definitions is null) return result;
        foreach (var definition in definitions)
        {
            if (definition.IsSeparator || definition.Match is not null && !definition.Match.IsMatch(facts)) continue;
            result.Add(new ResolvedItem.Action(
                definition.Name,
                new(ActionKind.UserCommand, definition.Command, workingDirectory, definition.RunAsAdmin, definition.Icon),
                definition.Icon,
                source));
        }
        return result;
    }

    private static List<ResolvedItem> NormalizeSeparators(List<ResolvedItem> items)
    {
        var result = new List<ResolvedItem>(items.Count);
        foreach (var item in items)
        {
            if (item is ResolvedItem.Separator && (result.Count == 0 || result[^1] is ResolvedItem.Separator)) continue;
            result.Add(item);
        }
        while (result.Count > 0 && result[0] is ResolvedItem.Separator) result.RemoveAt(0);
        while (result.Count > 0 && result[^1] is ResolvedItem.Separator) result.RemoveAt(result.Count - 1);
        return result;
    }
}
