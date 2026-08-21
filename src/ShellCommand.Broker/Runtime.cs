using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ShellCommand.Config.Yaml;
using ShellCommand.Core;

namespace ShellCommand.Broker;

public readonly record struct FileFingerprint(long Length, long LastWriteUtcTicks);

public sealed class FileConfigRuntime
{
    private sealed class Entry
    {
        public required string Path { get; init; }
        public FileFingerprint Fingerprint { get; set; }
        public object? Model { get; set; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = Array.Empty<Diagnostic>();
    }

    private readonly ConfigParser _parser;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _globalPath;

    public FileConfigRuntime(ConfigParser? parser = null, string? globalPath = null)
    {
        _parser = parser ?? new ConfigParser();
        _globalPath = globalPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "config", "global.shellcommand.yaml");
    }

    public string GlobalPath => _globalPath;

    public ConfigSnapshot Load(string workingDirectory, bool forceRefresh = false)
    {
        var diagnostics = new List<Diagnostic>();
        var global = LoadOne<GlobalConfig>(_globalPath, false, forceRefresh, diagnostics);
        var localPath = Path.Combine(Path.GetFullPath(workingDirectory), ".shellcommand.yaml");
        var local = LoadOne<DirectoryConfig>(localPath, true, forceRefresh, diagnostics);
        return new ConfigSnapshot(global, local, diagnostics);
    }

    public static ParseResult<T> Validate<T>(string path, bool global) where T : class
    {
        if (!File.Exists(path)) return new ParseResult<T>(null, new[] { new Diagnostic(path, DiagnosticSeverity.Error, "FILE_NOT_FOUND", "Configuration file does not exist.") });
        var text = ReadBounded(path, out var readError);
        if (readError is not null) return new ParseResult<T>(null, new[] { readError });
        if (global && typeof(T) == typeof(GlobalConfig)) return (ParseResult<T>)(object)ConfigParser.ParseGlobal(text!, path);
        if (!global && typeof(T) == typeof(DirectoryConfig)) return (ParseResult<T>)(object)ConfigParser.ParseDirectory(text!, path);
        return new ParseResult<T>(default, new[] { new Diagnostic(path, DiagnosticSeverity.Error, "INTERNAL_ERROR", "Unexpected configuration type.") });
    }

    private T? LoadOne<T>(string path, bool local, bool forceRefresh, List<Diagnostic> diagnostics) where T : class
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            _entries.TryRemove(path, out _);
            return null;
        }

        var info = new FileInfo(path);
        var fingerprint = new FileFingerprint(info.Length, info.LastWriteTimeUtc.Ticks);
        if (!forceRefresh && _entries.TryGetValue(path, out var cached) && cached.Model is T cachedModel && cached.Fingerprint == fingerprint)
        {
            diagnostics.AddRange(cached.Diagnostics);
            return cachedModel;
        }

        var text = ReadBounded(path, out var readError);
        if (readError is not null)
        {
            diagnostics.Add(readError);
            return _entries.TryGetValue(path, out var old) ? old.Model as T : null;
        }

        object result = local ? ConfigParser.ParseDirectory(text!, path) : ConfigParser.ParseGlobal(text!, path);
        var resultDiagnostics = local
            ? ((ParseResult<DirectoryConfig>)result).Diagnostics
            : ((ParseResult<GlobalConfig>)result).Diagnostics;
        diagnostics.AddRange(resultDiagnostics);
        object? model = local ? ((ParseResult<DirectoryConfig>)result).Value : ((ParseResult<GlobalConfig>)result).Value;
        if (model is not null)
        {
            _entries[path] = new Entry { Path = path, Fingerprint = fingerprint, Model = model, Diagnostics = resultDiagnostics };
            return model as T;
        }

        // Invalid edits retain an independently cached Last Known Good model.
        return _entries.TryGetValue(path, out var lkg) ? lkg.Model as T : null;
    }

    private static string? ReadBounded(string path, out Diagnostic? error)
    {
        error = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > ConfigParser.MaxFileBytes)
            {
                error = new Diagnostic(path, DiagnosticSeverity.Error, "VALUE_TOO_LONG", $"Configuration exceeds {ConfigParser.MaxFileBytes} bytes.");
                return null;
            }
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            error = new Diagnostic(path, DiagnosticSeverity.Error, "FILE_READ", ex.Message);
            return null;
        }
    }
}

public sealed class FileSystemDirectoryFacts : IDirectoryFacts
{
    private readonly string _directory;
    private IReadOnlyList<string>? _entries;

    public FileSystemDirectoryFacts(string directory) => _directory = directory;

    public bool Exists(string leafName)
    {
        _entries ??= Directory.EnumerateFileSystemEntries(_directory).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToArray();
        return leafName.Contains('*') || leafName.Contains('?')
            ? _entries.Any(value => MatchExpression.WildcardMatch(leafName, value))
            : _entries.Contains(leafName, StringComparer.OrdinalIgnoreCase);
    }
}

public interface IActionExecutor
{
    Task ExecuteAsync(ActionSpec spec, CancellationToken cancellationToken);
}

public sealed class ProcessActionExecutor : IActionExecutor
{
    private readonly string _appPath;
    private readonly string _globalConfigPath;

    public ProcessActionExecutor(string appPath, string globalConfigPath)
    {
        _appPath = appPath;
        _globalConfigPath = globalConfigPath;
    }

    public Task ExecuteAsync(ActionSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = spec.Kind switch
        {
            ActionKind.CopyPath => BuildCopyPath(spec.WorkingDirectory),
            ActionKind.EditGlobal => new ProcessStartInfo("notepad.exe", Quote(_globalConfigPath)) { UseShellExecute = true },
            ActionKind.OpenApp => new ProcessStartInfo(_appPath) { UseShellExecute = true },
            ActionKind.UserCommand => BuildUserCommand(spec),
            _ => throw new InvalidOperationException("Unknown action kind.")
        };
        if (spec.RunAsAdmin) startInfo.Verb = "runas";
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private static ProcessStartInfo BuildUserCommand(ActionSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Command) || string.IsNullOrWhiteSpace(spec.WorkingDirectory)) throw new InvalidOperationException("Action plan is incomplete.");
        var argv = WindowsCommandLine.Parse(spec.Command);
        if (argv.Count == 0) throw new InvalidOperationException("Command line is empty.");
        var info = new ProcessStartInfo(argv[0]) { WorkingDirectory = spec.WorkingDirectory, UseShellExecute = true };
        foreach (var argument in argv.Skip(1)) info.ArgumentList.Add(argument);
        return info;
    }

    private static ProcessStartInfo BuildCopyPath(string? workingDirectory)
    {
        var path = workingDirectory ?? Environment.CurrentDirectory;
        var escaped = path.Replace("'", "''", StringComparison.Ordinal);
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = path };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add($"Set-Clipboard -Value '{escaped}'");
        return info;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}

internal static class WindowsCommandLine
{
    public static IReadOnlyList<string> Parse(string commandLine)
    {
        if (!OperatingSystem.IsWindows()) return commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ptr = CommandLineToArgvW(commandLine, out var count);
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("Invalid Windows command line.");
        try
        {
            var args = new string[count];
            for (var i = 0; i < count; i++) args[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(ptr, i * IntPtr.Size)) ?? string.Empty;
            return args;
        }
        finally { LocalFree(ptr); }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string commandLine, out int argc);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
