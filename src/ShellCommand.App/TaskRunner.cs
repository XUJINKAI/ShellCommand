using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using ShellCommand.Broker;
using ShellCommand.Core;
namespace ShellCommand.App;

public sealed class TaskRunner
{
    private Process? _process;
    private bool _stopRequested;
    public void Stop()
    {
        _stopRequested = true;
        try { if (_process is { HasExited: false }) _process.Kill(true); }
        catch (InvalidOperationException) { }
    }
    public async Task<TaskRecord> RunAsync(ExecutionRequest request, Action<string> output)
    {
        var record = new TaskRecord(request.Id, request.Plan.Title, "started", DateTimeOffset.UtcNow);
        TaskJournal.Save(record);
        var launched = false;
        try
        {
            if (request.Plan.Actions.Count is < 1 or > 256) throw new InvalidOperationException("执行计划大小非法。");
            foreach (var action in request.Plan.Actions)
            {
                if (_stopRequested) throw new OperationCanceledException();
                if (action.Kind == "settings") { new MainWindow().Show(); launched = true; continue; }
                if (action.Kind == "copy") { Clipboard.SetText(action.Text ?? ""); continue; }
                if (action.Kind == "open")
                {
                    Process.Start(new ProcessStartInfo(action.Text!) { UseShellExecute = true })?.Dispose(); launched = true; continue;
                }
                string? script = null;
                try
                {
                    var info = new ProcessStartInfo(action.Exe ?? "")
                    {
                        UseShellExecute = action.Admin,
                        WorkingDirectory = action.Cwd ?? "",
                        CreateNoWindow = action.Output != "normal"
                    };
                    if (action.Admin)
                    {
                        if (action.Output != "normal" || action.Env is not null || action.Kind != "run") throw new InvalidOperationException("非法提权组合。");
                        info.Verb = "runas";
                    }
                    if (action.Kind == "script")
                    {
                        var folder = Path.Combine(DeploymentPackage.DataRoot, "temp", "scripts"); Directory.CreateDirectory(folder);
                        script = Path.Combine(folder, Guid.NewGuid().ToString("N") + (action.Shell == "cmd" ? ".cmd" : ".ps1"));
                        File.WriteAllText(script, action.Shell == "cmd" ? "@chcp 65001>nul\r\n" + action.Text : action.Text, new UTF8Encoding(action.Shell != "cmd"));
                        info.FileName = action.Shell switch { "cmd" => "cmd.exe", "pwsh" => "pwsh.exe", "powershell" => "powershell.exe", _ => throw new InvalidOperationException("未知 shell。") };
                        if (action.Shell == "cmd") info.Arguments = "/d /s /c \"\"" + script + "\"\"";
                        else foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) info.ArgumentList.Add(argument);
                    }
                    else if (action.Kind == "run") { foreach (var argument in action.Args ?? []) info.ArgumentList.Add(argument); }
                    else throw new InvalidOperationException("未知动作。");
                    foreach (var pair in action.Env ?? new Dictionary<string, string>()) info.Environment[pair.Key] = pair.Value;
                    var monitor = action.Output != "normal" || action.Kind == "script";
                    if (action.Output != "normal") { info.RedirectStandardOutput = true; info.RedirectStandardError = true; }
                    using var process = Process.Start(info) ?? throw new InvalidOperationException("进程未启动。");
                    _process = process;
                    if (!monitor) { launched = true; _process = null; continue; }
                    async Task Drain(StreamReader reader)
                    {
                        var buffer = new char[4096];
                        while (true)
                        {
                            var count = await reader.ReadAsync(buffer); if (count == 0) break;
                            if (action.Output == "window") output(new string(buffer, 0, count));
                        }
                    }
                    var stdout = info.RedirectStandardOutput ? Drain(process.StandardOutput) : Task.CompletedTask;
                    var stderr = info.RedirectStandardError ? Drain(process.StandardError) : Task.CompletedTask;
                    await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
                    _process = null;
                    if (_stopRequested) throw new OperationCanceledException();
                    if (process.ExitCode != 0)
                    {
                        record = record with { Status = "failed", ExitCode = process.ExitCode, Message = "命令退出码：" + process.ExitCode };
                        TaskJournal.Save(record); return record;
                    }
                }
                finally { if (script is not null) try { File.Delete(script); } catch (IOException) { } }
            }
            record = record with { Status = launched ? "launched" : "completed", Message = launched ? "已启动" : "已完成" };
        }
        catch (OperationCanceledException) { record = record with { Status = "canceled", Message = "任务已停止" }; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { record = record with { Status = "canceled", Message = "已取消管理员授权" }; }
        catch (Exception ex) { record = record with { Status = "failed", Message = ex.Message }; }
        finally { _process = null; }
        TaskJournal.Save(record); return record;
    }
}
