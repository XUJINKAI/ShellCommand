#pragma warning disable CA1416
using Microsoft.Win32;
using System.Text.Json;
using ShellCommand.Core;

namespace ShellCommand.Broker;

public enum MenuEntryType { StaticVerb, LegacyCom, PackagedExplorerCommand, SystemUnknown }
public enum MenuScope { Files, Directory, DirectoryBackground, Drive, All, Other }
public enum MenuEntryState { Enabled, Blocked, HiddenByVerb, ReadOnly, Unknown, PendingExplorerRestart }

public sealed record MenuEntry(
    string Id,
    string DisplayName,
    MenuEntryType Type,
    MenuScope Scope,
    MenuEntryState State,
    string Source,
    string RegistrationPath,
    string? Clsid,
    string? CommandOrDll,
    bool CanModify);

public sealed record MenuOperationResult(bool Success, string Message, bool RequiresExplorerRestart = false);

public sealed class ContextMenuScanner
{
    private static readonly (string Path, MenuScope Scope)[] StaticScopes =
    {
        ("*\\shell", MenuScope.Files), ("Directory\\shell", MenuScope.Directory),
        ("Directory\\Background\\shell", MenuScope.DirectoryBackground), ("Drive\\shell", MenuScope.Drive),
        ("AllFilesystemObjects\\shell", MenuScope.All)
    };
    private static readonly (string Path, MenuScope Scope)[] ComScopes =
    {
        ("*\\shellex\\ContextMenuHandlers", MenuScope.Files), ("Directory\\shellex\\ContextMenuHandlers", MenuScope.Directory),
        ("Directory\\Background\\shellex\\ContextMenuHandlers", MenuScope.DirectoryBackground), ("Drive\\shellex\\ContextMenuHandlers", MenuScope.Drive)
    };

    public static IReadOnlyList<MenuEntry> Scan()
    {
        var result = new List<MenuEntry>();
        foreach (var hive in new[] { (RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM") })
        {
            using var baseKey = OpenClasses(hive.Item1, writable: false);
            if (baseKey is null) continue;
            foreach (var (path, scope) in StaticScopes) ScanStatic(baseKey, hive.Item2, path, scope, result);
            foreach (var (path, scope) in ComScopes) ScanCom(baseKey, hive.Item2, path, scope, result);
        }
        return result;
    }

    private static void ScanStatic(RegistryKey baseKey, string hive, string path, MenuScope scope, List<MenuEntry> result)
    {
        using var shell = baseKey.OpenSubKey(path);
        if (shell is null) return;
        foreach (var name in shell.GetSubKeyNames())
        {
            using var verb = shell.OpenSubKey(name);
            if (verb is null) continue;
            var display = verb.GetValue(null) as string ?? name;
            var command = verb.OpenSubKey("command")?.GetValue(null) as string;
            var hidden = verb.GetValue("ProgrammaticAccessOnly") is not null;
            var canModify = hive == "HKCU";
            result.Add(new MenuEntry($"{hive}:{path}:{name}", display, MenuEntryType.StaticVerb, scope, hidden ? MenuEntryState.HiddenByVerb : (canModify ? MenuEntryState.Enabled : MenuEntryState.ReadOnly), hive, $"{path}\\{name}", null, command, canModify));
        }
    }

    private static void ScanCom(RegistryKey baseKey, string hive, string path, MenuScope scope, List<MenuEntry> result)
    {
        using var handlers = baseKey.OpenSubKey(path);
        if (handlers is null) return;
        using var blocked = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
        foreach (var name in handlers.GetSubKeyNames())
        {
            using var handler = handlers.OpenSubKey(name);
            var clsid = handler?.GetValue(null) as string;
            var isBlocked = clsid is not null && blocked?.GetValue(clsid) is not null;
            result.Add(new MenuEntry($"{hive}:{path}:{name}", name, MenuEntryType.LegacyCom, scope, isBlocked ? MenuEntryState.Blocked : MenuEntryState.Enabled, hive, $"{path}\\{name}", clsid, null, clsid is not null));
        }
    }

    private static RegistryKey? OpenClasses(RegistryHive hive, bool writable)
    {
        try { return RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey("Software\\Classes", writable); }
        catch (Exception) { return null; }
    }
}

public sealed class ContextMenuManager
{
    private static readonly JsonSerializerOptions JournalJsonOptions = new() { WriteIndented = true };
    private sealed record JournalRecord(string OperationId, DateTimeOffset Timestamp, MenuEntry Entry, string TargetPath, string ValueName, RegistryValueSnapshot Previous, RegistryValueSnapshot Applied, bool Committed, bool Restored);
    private sealed record RegistryValueSnapshot(bool Exists, RegistryValueKind? Kind, string? Data);
    private readonly string _journalPath;

    public ContextMenuManager(string? stateDirectory = null)
    {
        var directory = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "state");
        _journalPath = Path.Combine(directory, "menu-journal.json");
    }

    public MenuOperationResult Disable(MenuEntry entry)
    {
        if (!entry.CanModify) return new(false, "This menu entry is read-only.");
        try
        {
            var (path, valueName) = Target(entry);
            using var key = OpenTarget(entry, writable: true);
            if (key is null) return new(false, "The registry entry is no longer available.");
            var previous = Capture(key, valueName);
            var applied = entry.Type == MenuEntryType.StaticVerb ? new RegistryValueSnapshot(true, RegistryValueKind.String, string.Empty) : new RegistryValueSnapshot(true, RegistryValueKind.String, string.Empty);
            var record = new JournalRecord(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, entry, path, valueName, previous, applied, false, false);
            WriteJournal(record);
            key.SetValue(valueName, string.Empty, RegistryValueKind.String);
            if (!Matches(key, valueName, applied)) return new(false, "The registry write could not be verified.");
            WriteJournal(record with { Committed = true });
            return new(true, "Entry disabled. Explorer restart may be required.", true);
        }
        catch (UnauthorizedAccessException) { return new(false, "Permission denied. Retry from an explicit elevated action."); }
        catch (IOException ex) { return new(false, ex.Message); }
    }

    public MenuOperationResult Restore(MenuEntry entry)
    {
        try
        {
            var records = ReadJournal().Where(x => x.Committed && !x.Restored && x.Entry.Id == entry.Id).ToArray();
            if (records.Length == 0) return new(false, "No committed ShellCommand change exists for this entry.");
            var record = records[^1];
            using var key = OpenTarget(entry, writable: true);
            if (key is null) return new(false, "The registry entry is no longer available.");
            if (!Matches(key, record.ValueName, record.Applied)) return new(false, "External change detected; restore was not applied.");
            RestoreValue(key, record.ValueName, record.Previous);
            WriteJournal(record with { Restored = true });
            return new(true, "Entry restored. Explorer restart may be required.", true);
        }
        catch (UnauthorizedAccessException) { return new(false, "Permission denied. Retry from an explicit elevated action."); }
        catch (IOException ex) { return new(false, ex.Message); }
    }

    private static (string Path, string ValueName) Target(MenuEntry entry)
        => entry.Type == MenuEntryType.StaticVerb ? (entry.RegistrationPath, "ProgrammaticAccessOnly") : (@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", entry.Clsid ?? string.Empty);

    private static RegistryKey? OpenTarget(MenuEntry entry, bool writable)
    {
        if (entry.Type == MenuEntryType.StaticVerb) return Registry.CurrentUser.OpenSubKey($"Software\\Classes\\{entry.RegistrationPath}", writable);
        return Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", writable);
    }

    private void WriteJournal(JournalRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        var all = ReadJournal().Where(x => x.OperationId != record.OperationId).Append(record).ToArray();
        var temp = _journalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, JournalJsonOptions));
        File.Move(temp, _journalPath, true);
    }

    private JournalRecord[] ReadJournal()
    {
        if (!File.Exists(_journalPath)) return Array.Empty<JournalRecord>();
        try { return JsonSerializer.Deserialize<JournalRecord[]>(File.ReadAllText(_journalPath)) ?? Array.Empty<JournalRecord>(); }
        catch (JsonException) { return Array.Empty<JournalRecord>(); }
    }

    private static RegistryValueSnapshot Capture(RegistryKey key, string name)
    {
        if (key.GetValueNames().All(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) return new(false, null, null);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind = key.GetValueKind(name);
        var data = value switch { byte[] bytes => Convert.ToBase64String(bytes), string[] strings => JsonSerializer.Serialize(strings), _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) };
        return new(true, kind, data);
    }

    private static bool Matches(RegistryKey key, string name, RegistryValueSnapshot expected) => Capture(key, name) == expected;

    private static void RestoreValue(RegistryKey key, string name, RegistryValueSnapshot snapshot)
    {
        if (!snapshot.Exists) { key.DeleteValue(name, false); return; }
        object value = snapshot.Kind switch
        {
            RegistryValueKind.Binary => Convert.FromBase64String(snapshot.Data ?? string.Empty),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(snapshot.Data ?? "[]") ?? Array.Empty<string>(),
            RegistryValueKind.DWord => int.Parse(snapshot.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(snapshot.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            _ => snapshot.Data ?? string.Empty
        };
        key.SetValue(name, value, snapshot.Kind ?? RegistryValueKind.String);
    }
}
