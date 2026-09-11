#pragma warning disable CA1416
using Microsoft.Win32;
using System.Text.Json;
using System.Diagnostics;
using System.Text;

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
        result.AddRange(ScanPackaged());
        return result;
    }

    private static MenuEntry[] ScanPackaged()
    {
        const string script = """$ErrorActionPreference='Stop'; $result=@(); Get-AppxPackage | Select-Object -First 500 | ForEach-Object { $p=$_; try { $m=Get-AppxPackageManifest -Package $p.PackageFullName; foreach($v in $m.SelectNodes("//*[local-name()='FileExplorerContextMenus']//*[local-name()='Verb']")) { $result+=@{ Name=$p.Name; Id=$v.Id; Clsid=$v.Clsid; Package=$p.PackageFullName } } } catch {} }; ConvertTo-Json -InputObject @($result) -Compress""";
        try
        {
            using var process = new Process { StartInfo = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = SnapshotRuntime.ReadBoundedAsync(process.StandardOutput, 512 * 1024, deadline.Token);
            var errors = SnapshotRuntime.ReadBoundedAsync(process.StandardError, 32768, deadline.Token);
            try
            {
                process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
                var json = output.GetAwaiter().GetResult(); errors.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("现代菜单扫描失败。");
                using var parsed = JsonDocument.Parse(json);
                return parsed.RootElement.EnumerateArray().Select(value => new MenuEntry(
                    value.GetProperty("Package").GetString() + ":" + value.GetProperty("Id").GetString(),
                    value.GetProperty("Name").GetString() + " · " + value.GetProperty("Id").GetString(),
                    MenuEntryType.PackagedExplorerCommand, MenuScope.Other, MenuEntryState.ReadOnly, "Package", value.GetProperty("Package").GetString()!,
                    value.GetProperty("Clsid").GetString(), null, false)).ToArray();
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception)
        {
            return [new("packaged-diagnostic", "现代菜单扫描不可用：" + ex.Message, MenuEntryType.SystemUnknown, MenuScope.Other, MenuEntryState.ReadOnly, "Windows", "", null, null, false)];
        }
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
            using var commandKey = verb.OpenSubKey("command");
            var command = commandKey?.GetValue(null) as string;
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
            result.Add(new MenuEntry($"{hive}:{path}:{name}", name, MenuEntryType.LegacyCom, scope, isBlocked ? MenuEntryState.Blocked : MenuEntryState.Enabled, hive, $"{path}\\{name}", clsid, null, Guid.TryParse(clsid, out _)));
        }
    }

    private static RegistryKey? OpenClasses(RegistryHive hive, bool writable)
    {
        try { using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default); return key.OpenSubKey("Software\\Classes", writable); }
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
        try
        {
            using var operationLock = Lock();
            var (path, valueName) = Target(entry);
            var journal = ReadJournal(); // Corruption blocks mutation before opening a writable key.
            using var key = OpenTarget(entry, writable: true);
            if (key is null) return new(false, "注册项已经不存在。");
            var previous = Capture(key, valueName);
            var applied = new RegistryValueSnapshot(true, RegistryValueKind.String, string.Empty);
            var existing = journal.LastOrDefault(r => !r.Restored && r.TargetPath == path && r.ValueName == valueName);
            if (existing is not null)
            {
                if (Matches(key, valueName, existing.Applied)) { WriteJournal(existing with { Committed = true }); return new(true, "此项已经禁用。"); }
                return new(false, "存在待恢复的修改记录，请先恢复后再操作。");
            }
            if (previous == applied) return new(false, "此项已被其他程序禁用，ShellCommand 不接管它的恢复。");
            var record = new JournalRecord(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, entry, path, valueName, previous, applied, false, false);
            WriteJournal(record);
            key.SetValue(valueName, string.Empty, RegistryValueKind.String);
            if (!Matches(key, valueName, applied)) return new(false, "注册表写入校验失败，已保留恢复记录。");
            WriteJournal(record with { Committed = true });
            return new(true, "已禁用；资源管理器可能需要重新启动。", true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or JsonException or InvalidOperationException or ArgumentException)
        { return new(false, "未完成修改：" + ex.Message); }
    }

    public MenuOperationResult Restore(MenuEntry entry)
    {
        try
        {
            using var operationLock = Lock();
            var (path, valueName) = Target(entry);
            // Include Prepared records: a crash may happen between the registry write and commit.
            var record = ReadJournal().LastOrDefault(r => !r.Restored && r.TargetPath == path && r.ValueName == valueName);
            if (record is null) return new(false, "没有本程序保存的恢复记录。");
            using var key = OpenTarget(entry, writable: true);
            if (key is null) return new(false, "注册项已经不存在。");
            if (!Matches(key, valueName, record.Previous))
            {
                if (!Matches(key, valueName, record.Applied)) return new(false, "检测到外部修改，未覆盖当前值。");
                RestoreValue(key, valueName, record.Previous);
                if (!Matches(key, valueName, record.Previous)) return new(false, "恢复校验失败，恢复记录已保留。");
            }
            WriteJournal(record with { Restored = true });
            return new(true, "已恢复；资源管理器可能需要重新启动。", true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or JsonException or InvalidOperationException or ArgumentException)
        { return new(false, "未完成恢复：" + ex.Message); }
    }
    private FileStream Lock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        return new FileStream(_journalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static readonly string[] WritableScopes = new[] { "*\\shell\\", "Directory\\shell\\", "Directory\\Background\\shell\\", "Drive\\shell\\", "AllFilesystemObjects\\shell\\" };
    private static (string Path, string ValueName) Target(MenuEntry entry)
    {
        if (entry.Type == MenuEntryType.StaticVerb && entry.Source == "HKCU" &&
            WritableScopes.Any(p => entry.RegistrationPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return (entry.RegistrationPath, "ProgrammaticAccessOnly");
        if (entry.Type == MenuEntryType.LegacyCom && Guid.TryParse(entry.Clsid, out var clsid))
            return (@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", clsid.ToString("B"));
        throw new InvalidOperationException("此项只读，不能通过当前用户机制修改。");
    }
    private static RegistryKey? OpenTarget(MenuEntry entry, bool writable)
    {
        var (path, _) = Target(entry);
        return entry.Type == MenuEntryType.StaticVerb ? Registry.CurrentUser.OpenSubKey("Software\\Classes\\" + path, writable)
            : writable ? Registry.CurrentUser.CreateSubKey(path) : Registry.CurrentUser.OpenSubKey(path);
    }
    private void WriteJournal(JournalRecord record)
    {
        var all = ReadJournal().Where(x => x.OperationId != record.OperationId).Append(record).ToArray();
        var temp = _journalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, JournalJsonOptions)); File.Move(temp, _journalPath, true);
    }
    private JournalRecord[] ReadJournal()
    {
        string text;
        try { text = Preparation.ReadText(_journalPath, 4 * 1024 * 1024); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        var records = JsonSerializer.Deserialize<JournalRecord[]>(text) ?? throw new InvalidDataException("恢复日志损坏；停止修改。");
        if (records.Length > 4096) throw new InvalidDataException("恢复日志过大。");
        foreach (var record in records)
        {
            if (record?.Entry is null || record.Entry.RegistrationPath is null) throw new InvalidDataException("恢复日志内容非法；停止修改。");
            var target = Target(record.Entry);
            if (target.Path != record.TargetPath || target.ValueName != record.ValueName || record.Previous is null || record.Applied is null)
                throw new InvalidDataException("恢复日志内容非法；停止修改。");
        }
        return records;
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
            RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(snapshot.Data ?? string.Empty),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(snapshot.Data ?? "[]") ?? Array.Empty<string>(),
            RegistryValueKind.DWord => int.Parse(snapshot.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(snapshot.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            _ => snapshot.Data ?? string.Empty
        };
        key.SetValue(name, value, snapshot.Kind ?? RegistryValueKind.String);
    }
}
