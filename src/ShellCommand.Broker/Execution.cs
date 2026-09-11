using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ShellCommand.Core;
namespace ShellCommand.Broker;
public interface IActionExecutor { Task ExecuteAsync(LaunchPlan plan, CancellationToken cancellationToken); }
public sealed class ProcessActionExecutor : IActionExecutor
{
    private readonly string _app;
    private readonly ConcurrentDictionary<int, Process> _active = new();
    public ProcessActionExecutor(string app) => _app = app;
    public async Task ExecuteAsync(LaunchPlan plan, CancellationToken cancellationToken)
    {
        var request = new ExecutionRequest(Guid.NewGuid(), plan);
        var record = new TaskRecord(request.Id, plan.Title, "queued", DateTimeOffset.UtcNow);
        TaskJournal.Save(record);
        try
        {
            if (_active.Count >= 32) throw new InvalidOperationException("运行中的任务超过 32 个，请关闭或停止部分任务。");
            var start = new ProcessStartInfo(_app) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
            start.ArgumentList.Add("--execute");
            var process = Process.Start(start) ?? throw new InvalidOperationException("执行进程启动失败。");
            var pid = process.Id; _active[pid] = process;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { _active.TryRemove(pid, out _); process.Dispose(); }
                throw;
            }
            _ = ReapAsync(pid, process);
        }
        catch (Exception ex)
        {
            TaskJournal.Save(record with { Status = "failed", Message = ex.Message });
            throw;
        }
    }
    private async Task ReapAsync(int pid, Process process)
    {
        try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { _active.TryRemove(pid, out _); process.Dispose(); }
    }
}
