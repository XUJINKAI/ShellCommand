using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShellCommand.Config.Yaml;
using ShellCommand.Core;
namespace ShellCommand.Broker;

public sealed record PrepareRequest(string? Directory, string DataDirectory, string AppDirectory, string? EditorPath = null, string? EditorText = null);
public sealed record PersistedSource(int Version, Dictionary<string, string> Texts, string? BadHash, IReadOnlyList<Diagnostic> Diagnostics, Dictionary<string, string>? BadTexts = null);

public static class Preparation
{
    public static PreparedSnapshot Prepare(PrepareRequest request)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalPath = Path.Combine(request.DataDirectory, "config", "global.shellcommand.yaml");
        var global = LoadSource(globalPath, request, dependencies);
        var local = request.Directory is null ? new SourceSnapshot(null, [], true)
            : LoadSource(Path.Combine(request.Directory, ".shellcommand.yaml"), request, dependencies);
        IReadOnlyList<string>? facts = null;
        if (request.Directory is not null)
        {
            try
            {
                // Never enumerate an unbounded directory. Partial facts are unknown.
                var entries = Directory.EnumerateFileSystemEntries(request.Directory).Take(10001).Select(Path.GetFileName).OfType<string>().ToArray();
                if (entries.Length <= 10000) facts = entries;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        var watchPaths = dependencies.Where(path =>
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return !path.StartsWith(@"\\", StringComparison.Ordinal) && new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Fixed; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return false; }
        }).ToArray();
        if (request.EditorPath is null)
        {
            TrimCache(Path.Combine(request.DataDirectory, "cache", "sources"), "*.json", 128, 32 * 1024 * 1024);
            TrimCache(Path.Combine(request.DataDirectory, "cache", "icons"), "*.ico", 2048, 16 * 1024 * 1024);
        }
        return new(global, local, facts, dependencies.ToArray(), DateTimeOffset.UtcNow, watchPaths);
    }
    private static void TrimCache(string folder, string pattern, int maxFiles, long maxBytes)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var files = new DirectoryInfo(folder).EnumerateFiles(pattern).Take(4096).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            long bytes = 0; var count = 0;
            foreach (var file in files)
            {
                bytes += file.Length;
                if (++count > maxFiles || bytes > maxBytes) try { file.Delete(); } catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
    }
    private static SourceSnapshot LoadSource(string root, PrepareRequest request, HashSet<string> dependencies)
    {
        root = Path.GetFullPath(root); dependencies.Add(root);
        var cache = Path.Combine(request.DataDirectory, "cache", "sources", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))) + ".json");
        PersistedSource? saved = null;
        MenuConfig? good = null;
        try
        {
            saved = JsonSerializer.Deserialize<PersistedSource>(ReadText(cache, 8 * 1024 * 1024));
            if (saved is not null && (saved.Texts is null || saved.Diagnostics is null)) throw new InvalidDataException("缓存缺少必要字段。");
            if (saved?.Version == 2) { if (saved.Texts.Count != 0) good = BuildTree(root, saved.Texts, dependencies); }
            else saved = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or DecoderFallbackException) { saved = null; }
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hash = "";
        try
        {
            if (saved?.BadTexts is { Count: > 0 } badTexts)
            {
                var unchanged = true;
                foreach (var pair in badTexts)
                {
                    dependencies.Add(pair.Key);
                    try { if (ReadConfig(pair.Key, request) != pair.Value) unchanged = false; }
                    catch (Exception readError) when (readError is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException) { unchanged = false; }
                }
                if (unchanged && Hash(badTexts) == saved.BadHash) return WithIcons(good, saved.Diagnostics, request);
            }
            try { texts[root] = ReadConfig(root, request); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // External editors often replace a file via rename. Confirm disappearance.
                Thread.Sleep(180);
                try { texts[root] = ReadConfig(root, request); }
                catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
                {
                    if (request.EditorPath is null) File.Delete(cache);
                    return new(null, [], true);
                }
            }
            // Read includes as a graph; all files publish as one source transaction.
            var total = Encoding.UTF8.GetByteCount(texts[root]);
            ReadIncludes(root, texts, dependencies, ref total, request);
            hash = Hash(texts);
            if (saved is not null && saved.BadHash == hash) return WithIcons(good, saved.Diagnostics, request);
            var model = BuildTree(root, texts, dependencies);
            if (request.EditorPath is null) Save(cache, new(2, texts, null, []));
            return WithIcons(model, [], request);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or DecoderFallbackException)
        {
            var diagnostics = ex is ConfigFailure failure ? failure.Diagnostics : new[] { new Diagnostic(root, DiagnosticSeverity.Error, "CONFIG_READ", ex.Message) };
            if (request.EditorPath is null && texts.Count != 0 && ex is ConfigFailure)
            {
                try { Save(cache, new(2, saved?.Texts ?? new(), Hash(texts), diagnostics, texts)); }
                catch (Exception saveError) when (saveError is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            return WithIcons(good, diagnostics, request);
        }
    }
    private static string ReadConfig(string path, PrepareRequest request) => string.Equals(path, request.EditorPath, StringComparison.OrdinalIgnoreCase) ? request.EditorText ?? "" : ReadText(path, ConfigParser.MaxFileBytes);
    private static void ReadIncludes(string path, Dictionary<string, string> texts, HashSet<string> dependencies, ref int total, PrepareRequest request)
    {
        var result = ConfigParser.Parse(texts[path], path);
        if (!result.IsValid) throw new ConfigFailure(result.Diagnostics);
        foreach (var include in result.Value!.Includes)
        {
            var child = Path.GetFullPath(include, Path.GetDirectoryName(path)!);
            dependencies.Add(child);
            if (texts.Count >= 8 || texts.ContainsKey(child)) throw new InvalidOperationException("include 循环、重复引用或超过 8 个文件。");
            var text = ReadConfig(child, request);
            total += Encoding.UTF8.GetByteCount(text);
            if (total > 1024 * 1024) throw new InvalidOperationException("include 总量超过 1 MiB。");
            texts.Add(child, text);
            ReadIncludes(child, texts, dependencies, ref total, request);
        }
    }
    public static MenuConfig BuildTree(string root, IReadOnlyDictionary<string, string> texts, HashSet<string>? dependencies = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nodes = 0;
        IReadOnlyList<MenuDefinition> Visit(string path)
        {
            dependencies?.Add(path);
            if (!seen.Add(path) || seen.Count > 8 || !texts.TryGetValue(path, out var text)) throw new InvalidOperationException("include 快照缺失、重复或循环。");
            var parsed = ConfigParser.Parse(text, path);
            if (!parsed.IsValid) throw new ConfigFailure(parsed.Diagnostics);
            var menu = new List<MenuDefinition>();
            foreach (var child in parsed.Value!.Includes) menu.AddRange(Visit(Path.GetFullPath(child, Path.GetDirectoryName(path)!)));
            void Check(IEnumerable<MenuDefinition> definitions)
            {
                foreach (var node in definitions)
                {
                    if (++nodes > 100 || !node.Separator && !ids.Add(node.Id)) throw new InvalidOperationException("来源树存在重复 ID 或超过 100 个节点。");
                    if (node.Items is not null) Check(node.Items);
                }
            }
            Check(parsed.Value.Menu); menu.AddRange(parsed.Value.Menu);
            return menu;
        }
        if (texts.Values.Sum(Encoding.UTF8.GetByteCount) > 1024 * 1024) throw new InvalidOperationException("快照过大。");
        return new(Visit(root), []);
    }
    private static SourceSnapshot WithIcons(MenuConfig? config, IReadOnlyList<Diagnostic> diagnostics, PrepareRequest request)
    {
        var all = diagnostics.ToList();
        var prepared = PrepareIcons(config, request, all);
        return new(prepared, all);
    }
    private static MenuConfig? PrepareIcons(MenuConfig? config, PrepareRequest request, List<Diagnostic> diagnostics)
    {
        if (config is null) return null;
        MenuDefinition Map(MenuDefinition node)
        {
            var icon = node.Icon;
            if (icon is not null)
            {
                try
                {
                    var reference = IconCache.Prepare(icon, node.SourcePath, request.DataDirectory, request.AppDirectory);
                    icon = new(File: reference);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { icon = null; diagnostics.Add(new(node.SourcePath, DiagnosticSeverity.Warning, "ICON", ex.Message, Field: node.Id + ".icon")); }
            }
            return node with { Icon = icon, Items = node.Items?.Select(Map).ToArray() };
        }
        return config with { Menu = config.Menu.Select(Map).ToArray() };
    }
    public static string ReadText(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[maxBytes + 1]; var length = 0;
        while (length < bytes.Length)
        {
            var count = stream.Read(bytes, length, bytes.Length - length); if (count == 0) break; length += count;
        }
        if (length > maxBytes) throw new InvalidDataException("文件大小超过限制：" + path);
        var offset = length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes, offset, length - offset);
    }
    private static string Hash(Dictionary<string, string> texts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(texts.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)))));
    private static void Save(string path, PersistedSource source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(source)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed class ConfigFailure : InvalidOperationException
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
        public ConfigFailure(IReadOnlyList<Diagnostic> diagnostics) : base("配置验证失败。") => Diagnostics = diagnostics;
    }
}
