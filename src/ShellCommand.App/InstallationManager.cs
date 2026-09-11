using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32;
using ShellCommand.Broker;

namespace ShellCommand.App;

public enum IntegrationState
{
    NotInstalled,
    Installed,
    NeedsRepair,
    PackageFilesMissing
}

public sealed record InstallationStatus(
    IntegrationState State,
    bool PackageRegistered,
    bool FilesPresent,
    bool AutoStartRegistered,
    bool BrokerRunning,
    string Message)
{
    public bool IsInstalled => State == IntegrationState.Installed;
}

public sealed record InstallationOperationResult(bool Success, string Message, InstallationStatus Status);

public sealed record InstalledBuild(string Root, bool DeveloperRegistration);

public sealed class InstallationManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ShellCommand11.Broker";
    private static string RecordPath => Path.Combine(DeploymentPackage.DataRoot, "state", "installation.json");
    private readonly string _sourceRoot = NormalizeDirectory(AppContext.BaseDirectory);
    public string AppRoot => ReadRecord()?.Root ?? _sourceRoot;
    public static string GlobalConfigPath => Path.Combine(DeploymentPackage.DataRoot, "config", "global.shellcommand.yaml");

    private static InstalledBuild? ReadRecord()
    {
        try
        {
            using var stream = File.OpenRead(RecordPath);
            if (stream.Length > 4096) throw new InvalidDataException("安装记录过大。");
            var record = System.Text.Json.JsonSerializer.Deserialize<InstalledBuild>(stream) ?? throw new InvalidDataException("安装记录损坏。");
            var runner = NormalizeDirectory(Path.Combine(DeploymentPackage.DataRoot, "runner"));
            if (!string.Equals(Path.GetDirectoryName(record.Root), runner, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装记录指向 runner 以外的目录。");
            return record;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void WriteRecord(InstalledBuild record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecordPath)!);
        var temporary = RecordPath + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(record));
        File.Move(temporary, RecordPath, true);
    }

    public async Task<InstallationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var record = ReadRecord();
        var root = record?.Root ?? _sourceRoot;
        var files = await Task.Run(() => { try { DeploymentPackage.Validate(root); return true; } catch (Exception) { return false; } }, cancellationToken).ConfigureAwait(false);
        var package = await RunPowerShellAsync("$ErrorActionPreference='Stop'; $p=Get-AppxPackage -Name 'ShellCommand11'; if($p){$p.PackageFullName}", cancellationToken).ConfigureAwait(false);
        if (!package.Success) throw new InvalidOperationException(package.ErrorOrOutput);
        var registered = !string.IsNullOrWhiteSpace(package.Output);
        var correctLocation = registered && string.Equals(RegisteredLocation(package.Output), NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase);
        var broker = record is not null && await PingBrokerAsync(root, cancellationToken).ConfigureAwait(false);
        var autoStart = string.Equals(ReadAutoStart(), Quote(Path.Combine(root, "ShellCommand.Broker.exe")), StringComparison.OrdinalIgnoreCase);
        var state = !registered ? IntegrationState.NotInstalled : !files ? IntegrationState.PackageFilesMissing : record is not null && correctLocation && broker && autoStart ? IntegrationState.Installed : IntegrationState.NeedsRepair;
        var message = state == IntegrationState.Installed
            ? (record!.DeveloperRegistration ? "开发注册已启用；尚未通过正式签名安装验证。" : "已安装，后台协议与 runner 路径检查通过。")
            : registered ? "集成需要修复，请检查 runner、注册与后台。" : "尚未启用右键菜单集成。";
        return new(state, registered, files, autoStart, broker, message);
    }

    public async Task<InstallationOperationResult> InstallOrRepairAsync(bool developerRegistration = false, CancellationToken cancellationToken = default)
    {
        InstalledBuild? previous = null;
        string? previousAutoStart = null;
        bool registrationAttempted = false;
        FileStream? operationLock = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath)!);
            operationLock = new FileStream(RecordPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            previous = ReadRecord();
            previousAutoStart = ReadAutoStart();
            var target = await Task.Run(() => DeploymentPackage.Stage(_sourceRoot, DeploymentPackage.DataRoot), cancellationToken).ConfigureAwait(false);
            var next = new InstalledBuild(target, developerRegistration);
            if (!developerRegistration && !File.Exists(Path.Combine(target, "ShellCommand.Identity.msix")))
                throw new InvalidOperationException("本开发构建未附带签名身份包，不能正式启用。开发测试请使用 --developer-install；不会自动打开 Windows 开发者模式。");
            foreach (var folder in new[] { "config", "state", "cache", "logs", "temp" }) Directory.CreateDirectory(Path.Combine(DeploymentPackage.DataRoot, folder));
            // P0 deliberately does not generate a legacy template. P1 installs a v2 template.
            StopInstalledBroker();
            registrationAttempted = true;
            await UnregisterAsync(cancellationToken).ConfigureAwait(false);
            await RegisterAsync(next, cancellationToken).ConfigureAwait(false);
            WriteAutoStart(Quote(Path.Combine(target, "ShellCommand.Broker.exe")));
            await EnsureBrokerAsync(target, cancellationToken).ConfigureAwait(false);
            WriteRecord(next);
            var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!status.IsInstalled) throw new InvalidOperationException("安装后健康检查失败。");
            return new(true, status.Message + " 程序已复制到 " + target, status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or System.Text.Json.JsonException or OperationCanceledException)
        {
            var message = "安装失败：" + ex.Message;
            if (registrationAttempted)
            {
                try
                {
                    StopInstalledBroker();
                    if (previous is null)
                    {
                        await UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
                        File.Delete(RecordPath);
                    }
                    else
                    {
                        await UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
                        await RegisterAsync(previous, CancellationToken.None).ConfigureAwait(false);
                        WriteRecord(previous);
                        await EnsureBrokerAsync(previous.Root, CancellationToken.None).ConfigureAwait(false);
                    }
                    WriteAutoStart(previousAutoStart);
                    message += " 已恢复先前安装状态。";
                }
                catch (Exception rollback) { message += " 恢复失败：" + rollback.Message + "；可运行 --disable-integration 注销。"; }
            }
            return new(false, message, new(IntegrationState.NeedsRepair, false, false, false, false, message));
        }
        finally { operationLock?.Dispose(); }
    }

    private static async Task RegisterAsync(InstalledBuild build, CancellationToken cancellationToken)
    {
        var path = Path.Combine(build.Root, build.DeveloperRegistration ? "AppxManifest.xml" : "ShellCommand.Identity.msix");
        var mode = build.DeveloperRegistration ? "-Register" : "-Path";
        var result = await RunPowerShellAsync($"$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; Add-AppxPackage {mode} {PowerShellLiteral(path)} -ExternalLocation {PowerShellLiteral(build.Root)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException("Windows 集成注册失败：" + result.ErrorOrOutput);
        // Verify the actual registered external location, not just a package name.
        var verify = await RunPowerShellAsync("$ErrorActionPreference='Stop'; (Get-AppxPackage -Name 'ShellCommand11').PackageFullName", cancellationToken).ConfigureAwait(false);
        if (!verify.Success || string.IsNullOrWhiteSpace(verify.Output) || !string.Equals(RegisteredLocation(verify.Output), NormalizeDirectory(build.Root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows 未报告预期的 runner 注册位置。" + verify.ErrorOrOutput);
    }

    private static string? RegisteredLocation(string fullName)
    {
        uint length = 0;
        const int effectiveExternal = 5;
        if (GetPackagePathByFullName2(fullName, effectiveExternal, ref length, null) != 122 || length is 0 or > 32768) return null;
        var buffer = new char[length];
        if (GetPackagePathByFullName2(fullName, effectiveExternal, ref length, buffer) != 0) return null;
        return NormalizeDirectory(new string(buffer, 0, checked((int)length - 1)));
    }

    [System.Runtime.InteropServices.DllImport("kernelbase.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName2(string packageFullName, int packagePathType, ref uint pathLength, [System.Runtime.InteropServices.Out] char[]? path);

    private static async Task UnregisterAsync(CancellationToken cancellationToken)
    {
        var result = await RunPowerShellAsync("$ErrorActionPreference='Stop'; Get-AppxPackage -Name 'ShellCommand11' | Remove-AppxPackage", cancellationToken).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException(result.ErrorOrOutput);
    }

    public static async Task<InstallationOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath)!);
            using var operationLock = new FileStream(RecordPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // No configuration or installation record is needed for emergency removal.
            await UnregisterAsync(cancellationToken).ConfigureAwait(false);
            WriteAutoStart(null);
            StopInstalledBroker();
            File.Delete(RecordPath);
            foreach (var folder in new[] { "cache", "temp", "runner" })
            {
                var path = Path.Combine(DeploymentPackage.DataRoot, folder);
                try { if (Directory.Exists(path)) Directory.Delete(path, true); }
                catch (IOException) { /* loaded binaries remain for later cleanup */ }
                catch (UnauthorizedAccessException) { }
            }
            return new(true, "已注销集成和自启动，保留配置及菜单恢复记录。占用中的程序文件将在关闭相关进程后才能删除。", new(IntegrationState.NotInstalled, false, false, false, false, "已注销"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            return new(false, "卸载失败：" + ex.Message, new(IntegrationState.NeedsRepair, false, false, false, false, ex.Message));
        }
    }

    public static void RestartExplorer()
    {
        StopSessionProcesses("explorer", false);
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
    }

    private static void StopInstalledBroker() => StopSessionProcesses("ShellCommand.Broker", true);
    private static void StopSessionProcesses(string name, bool requireRunner)
    {
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session) continue;
                    if (requireRunner && !(process.MainModule?.FileName?.StartsWith(Path.Combine(DeploymentPackage.DataRoot, "runner") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
    }

    private static async Task EnsureBrokerAsync(string root, CancellationToken cancellationToken)
    {
        if (await PingBrokerAsync(root, cancellationToken).ConfigureAwait(false)) return;
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(root, "ShellCommand.Broker.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root });
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            if (await PingBrokerAsync(root, cancellationToken).ConfigureAwait(false)) return;
        }
        throw new InvalidOperationException("后台未在预期 runner 响应健康检查。");
    }

    private static async Task<bool> PingBrokerAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.DefaultPipeName(), PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(300));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await PipeProtocol.WriteAsync(pipe, new(1, MessageType.PingRequest, []), timeout.Token).ConfigureAwait(false);
            var response = await PipeProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
            var offset = 0;
            var actualRoot = PipeProtocol.ReadString(response.Payload, ref offset);
            return response.Type == MessageType.PingResponse && response.RequestId == 1 && offset == response.Payload.Length && string.Equals(NormalizeDirectory(actualRoot), NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (IOException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static string? ReadAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
    private static void WriteAutoStart(string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (value is null) key.DeleteValue(RunValueName, false);
        else key.SetValue(RunValueName, value, RegistryValueKind.String);
    }

    private static async Task<PowerShellResult> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-EncodedCommand");
        process.StartInfo.ArgumentList.Add(encoded);
        if (!process.Start()) throw new InvalidOperationException("无法启动 Windows PowerShell。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new(false, string.Empty, "Windows PowerShell 操作超时。");
        }
        return new(process.ExitCode == 0, (await outputTask.ConfigureAwait(false)).Trim(), (await errorTask.ConfigureAwait(false)).Trim());
    }

    private static string PowerShellLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    private static string NormalizeDirectory(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed record PowerShellResult(bool Success, string Output, string Error)
    {
        public string ErrorOrOutput => string.IsNullOrWhiteSpace(Error) ? Output : Error;
    }
}
