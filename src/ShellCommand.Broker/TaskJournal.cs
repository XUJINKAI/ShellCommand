using System.Text.Json;
using ShellCommand.Core;
namespace ShellCommand.Broker;
public sealed record ExecutionRequest(Guid Id, LaunchPlan Plan);
public sealed record TaskRecord(Guid Id, string Title, string Status, DateTimeOffset Created, string? Message = null, int? ExitCode = null);
public static class TaskJournal
{
    public static string Folder => Path.Combine(DeploymentPackage.DataRoot, "state", "tasks");
    public static void Save(TaskRecord record)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, record.Id.ToString("N") + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record)); File.Move(temporary, path, true);
        foreach (var old in new DirectoryInfo(Folder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(200))
            try { old.Delete(); } catch (IOException) { }
    }
    public static IReadOnlyList<TaskRecord> Read()
    {
        if (!Directory.Exists(Folder)) return [];
        var records = new List<TaskRecord>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json").Take(250))
            try { var record = JsonSerializer.Deserialize<TaskRecord>(Preparation.ReadText(path, 65536)); if (record is not null) records.Add(record); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return records.OrderByDescending(r => r.Created).Take(200).ToArray();
    }
}
