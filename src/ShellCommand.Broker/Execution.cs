using System.Diagnostics;
using System.Text.Json;
using ShellCommand.Core;
namespace ShellCommand.Broker;

public interface IActionExecutor { Task ExecuteAsync(LaunchPlan plan, CancellationToken cancellationToken); }
public sealed class ProcessActionExecutor : IActionExecutor
{
    private readonly string _app;
    public ProcessActionExecutor(string app) => _app = app;
    public async Task ExecuteAsync(LaunchPlan plan, CancellationToken cancellationToken)
    {
        // No command text is accepted over the public pipe. Only a consumed server token reaches here.
        var start = new ProcessStartInfo(_app) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
        start.ArgumentList.Add("--execute");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("执行进程启动失败。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(plan).AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
    }
}
