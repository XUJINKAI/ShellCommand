using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ShellCommand.Core;
namespace ShellCommand.Broker;

public sealed class SnapshotRuntime : IDisposable
{
    private sealed record Entry(PreparedSnapshot Snapshot, DateTimeOffset Used, int Bytes);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(64);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _dataDirectory;
    private readonly string _appDirectory;
    private readonly string _brokerPath;
    private readonly ResolveEnvironment _environment;
    private SourceSnapshot _global = new(null, []);
    public SnapshotRuntime(string? dataDirectory = null, string? appDirectory = null)
    {
        _dataDirectory = dataDirectory ?? DeploymentPackage.DataRoot;
        _appDirectory = appDirectory ?? AppContext.BaseDirectory;
        _brokerPath = Path.Combine(_appDirectory, "ShellCommand.Broker.exe");
        _environment = new(_appDirectory, _dataDirectory, Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .GroupBy(p => (string)p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (string)g.Last().Value!, StringComparer.OrdinalIgnoreCase));
        _ = Task.Run(WorkerAsync); _ = Task.Run(WorkerAsync);
        Request("");
    }
    public ResolvedMenu Resolve(MenuContext context)
    {
        // This path does not open files, enumerate directories or start processes.
        var key = context.Directory ?? "";
        Entry? entry; SourceSnapshot global;
        lock (_gate)
        {
            _entries.TryGetValue(key, out entry); global = _global;
            if (entry is not null) _entries[key] = entry with { Used = DateTimeOffset.UtcNow };
        }
        var stale = entry is null || DateTimeOffset.UtcNow - entry.Snapshot.PreparedAt > TimeSpan.FromSeconds(30);
        if (stale) Request(key);
        var facts = stale ? null : entry!.Snapshot.Facts;
        var local = entry?.Snapshot.Local;
        var result = MenuResolver.Resolve(global.Config, local?.Config, facts, context, _environment);
        return result with { Diagnostics = global.Diagnostics.Concat(local?.Diagnostics ?? []).Concat(result.Diagnostics).ToArray() };
    }
    public void Refresh(string? directory)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(directory ?? "", out var entry)) _entries[directory ?? ""] = entry with { Snapshot = entry.Snapshot with { PreparedAt = DateTimeOffset.MinValue } };
        }
        Request(directory ?? "");
    }
    private void Request(string key)
    {
        if (!_pending.TryAdd(key, 0)) return;
        if (!_queue.Writer.TryWrite(key)) _pending.TryRemove(key, out _);
    }
    private async Task WorkerAsync()
    {
        try
        {
            await foreach (var key in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await Task.Delay(100, _stop.Token).ConfigureAwait(false); // one-shot coalescing of editor/file event bursts
                    var snapshot = await PrepareAsync(new(key.Length == 0 ? null : key, _dataDirectory, _appDirectory), _brokerPath, _stop.Token).ConfigureAwait(false);
                    var bytes = checked(JsonSerializer.Serialize(snapshot).Length * 4);
                    lock (_gate)
                    {
                        _global = snapshot.Global;
                        _entries[key] = new(snapshot, DateTimeOffset.UtcNow, bytes);
                        while (_entries.Count > 128 || _entries.Values.Sum(e => e.Bytes) > 32 * 1024 * 1024) _entries.Remove(_entries.MinBy(p => p.Value.Used).Key);
                    }
                    Watch(snapshot.WatchPaths ?? [], key);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or JsonException) { }
                finally { _pending.TryRemove(key, out _); }
            }
        }
        catch (OperationCanceledException) { }
    }
    public static async Task<PreparedSnapshot> PrepareAsync(PrepareRequest request, string brokerPath, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new(brokerPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add("--prepare");
        if (!process.Start()) throw new InvalidOperationException("无法启动配置准备进程。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var error = ReadBoundedAsync(process.StandardError, 32768, timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            var output = await ReadBoundedAsync(process.StandardOutput, 4 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException(await error.ConfigureAwait(false));
            return JsonSerializer.Deserialize<PreparedSnapshot>(output) ?? throw new InvalidDataException("准备结果为空。");
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            // Do not release this worker's process quota until the child actually exits.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await error.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }
    public static async Task<string> ReadBoundedAsync(TextReader reader, int limit, CancellationToken cancellationToken)
    {
        var output = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); if (count == 0) break;
            if (output.Length + count > limit) throw new InvalidDataException("进程输出超过限制。");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
    private void Watch(IReadOnlyList<string> dependencies, string key)
    {
        // Watchers are a bounded optimization; next-menu age checks handle missed events.
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            foreach (var path in dependencies.Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_watchers.ContainsKey(path)) continue;
                if (_watchers.Count >= 32)
                {
                    var oldest = _watchers.Keys.FirstOrDefault(p => !p.StartsWith(Path.Combine(_dataDirectory, "config"), StringComparison.OrdinalIgnoreCase));
                    if (oldest is null) continue;
                    _watchers.Remove(oldest, out var expired); expired!.Dispose();
                }
                try
                {
                    var watcher = new FileSystemWatcher(path, "*") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler changed = (_, _) => RefreshAffected(path, key);
                    watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed;
                    watcher.Renamed += (_, _) => RefreshAffected(path, key);
                    watcher.Error += (_, _) => RefreshAffected(path, key);
                    watcher.EnableRaisingEvents = true; _watchers.Add(path, watcher);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
            }
        }
    }
    private void RefreshAffected(string directory, string fallback)
    {
        string[] keys;
        lock (_gate)
            keys = _entries.Where(pair => pair.Key.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.Snapshot.Dependencies.Any(p => string.Equals(Path.GetDirectoryName(p), directory, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.Key).Append(fallback).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var key in keys) Refresh(key);
    }
    public void Dispose()
    {
        _stop.Cancel(); _queue.Writer.TryComplete();
        lock (_gate) { foreach (var watcher in _watchers.Values) watcher.Dispose(); _watchers.Clear(); }
    }
}
