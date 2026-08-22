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

/// <summary>
/// User-facing installation and repair operations for the portable package.
/// The PowerShell calls are internal implementation details; users never need to run a script.
/// </summary>
public sealed class InstallationManager
{
    private const string PackageName = "ShellCommand11";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ShellCommand11.Broker";

    public string AppRoot { get; } = NormalizeDirectory(AppContext.BaseDirectory);
    public string AppPath => Path.Combine(AppRoot, "ShellCommand.exe");
    public string BrokerPath => Path.Combine(AppRoot, "ShellCommand.Broker.exe");
    public string ExplorerDllPath => Path.Combine(AppRoot, "ShellCommand.Explorer.dll");
    public string ManifestPath => Path.Combine(AppRoot, "AppxManifest.xml");
    public static string GlobalConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "config", "global.shellcommand.yaml");

    public async Task<InstallationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var filesPresent = File.Exists(AppPath) && File.Exists(BrokerPath) && File.Exists(ExplorerDllPath) && File.Exists(ManifestPath);
        var packageTask = IsPackageRegisteredAsync(cancellationToken);
        var brokerTask = PingBrokerAsync(cancellationToken);
        var packageRegistered = await packageTask.ConfigureAwait(false);
        var brokerRunning = await brokerTask.ConfigureAwait(false);
        var autoStartRegistered = IsAutoStartRegistered();

        var state = !packageRegistered
            ? IntegrationState.NotInstalled
            : !filesPresent
                ? IntegrationState.PackageFilesMissing
                : autoStartRegistered
                    ? IntegrationState.Installed
                    : IntegrationState.NeedsRepair;

        var message = state switch
        {
            IntegrationState.NotInstalled => filesPresent ? "尚未安装右键菜单集成" : "安装包文件不完整，无法安装",
            IntegrationState.Installed when brokerRunning => "已安装，ShellCommand 正在运行",
            IntegrationState.Installed => "已安装，但后台服务尚未响应",
            IntegrationState.PackageFilesMissing => "当前注册的集成找不到完整程序文件",
            _ => "安装不完整，需要修复"
        };
        return new(state, packageRegistered, filesPresent, autoStartRegistered, brokerRunning, message);
    }

    public async Task<InstallationOperationResult> InstallOrRepairAsync(CancellationToken cancellationToken = default)
    {
        var before = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!before.FilesPresent)
            return new(false, "当前目录缺少安装所需文件。请从完整的 ShellCommand ZIP 包中运行 ShellCommand.exe。", before);

        try
        {
            var registration = await RunPowerShellAsync($"$ErrorActionPreference = 'Stop'; Add-AppxPackage -Register {PowerShellLiteral(ManifestPath)} -ExternalLocation {PowerShellLiteral(AppRoot)} -ForceApplicationShutdown", cancellationToken).ConfigureAwait(false);
            if (!registration.Success)
                return new(false, "Windows 集成注册失败：" + registration.ErrorOrOutput, await GetStatusAsync(cancellationToken).ConfigureAwait(false));

            RegisterAutoStart();
            EnsureGlobalConfig();
            await EnsureBrokerAsync(cancellationToken).ConfigureAwait(false);
            var after = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return after.PackageRegistered && after.FilesPresent && after.AutoStartRegistered
                ? new(true, "ShellCommand 已安装。右键菜单可能需要重启 Explorer 才会刷新。", after)
                : new(false, "安装流程已执行，但状态检查未通过：" + after.Message, after);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            var status = await GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            return new(false, "安装失败：" + ex.Message, status);
        }
    }

    public async Task<InstallationOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var registration = await RunPowerShellAsync($"$ErrorActionPreference = 'Stop'; Get-AppxPackage -Name {PowerShellLiteral(PackageName)} | Remove-AppxPackage", cancellationToken).ConfigureAwait(false);
            if (!registration.Success)
                return new(false, "Windows 集成注销失败：" + registration.ErrorOrOutput, await GetStatusAsync(cancellationToken).ConfigureAwait(false));

            RemoveAutoStart();
            StopInstalledBroker();
            var after = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return !after.PackageRegistered && !after.AutoStartRegistered
                ? new(true, "ShellCommand 已卸载。用户配置文件已保留。右键菜单可能需要重启 Explorer。", after)
                : new(false, "卸载流程已执行，但状态检查未通过：" + after.Message, after);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            var status = await GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            return new(false, "卸载失败：" + ex.Message, status);
        }
    }

    public static void RestartExplorer()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try { process.Kill(); process.WaitForExit(3000); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    private async Task<bool> IsPackageRegisteredAsync(CancellationToken cancellationToken)
    {
        var result = await RunPowerShellAsync($"$package = Get-AppxPackage -Name {PowerShellLiteral(PackageName)} | Where-Object {{ $_.InstallLocation.TrimEnd('\\') -ieq {PowerShellLiteral(AppRoot)} }} | Select-Object -First 1; if ($null -ne $package) {{ 'true' }} else {{ 'false' }}", cancellationToken).ConfigureAwait(false);
        return result.Success && string.Equals(result.Output.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureBrokerAsync(CancellationToken cancellationToken)
    {
        if (await PingBrokerAsync(cancellationToken).ConfigureAwait(false)) return;
        var process = Process.Start(new ProcessStartInfo(BrokerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppRoot
        });
        process?.Dispose();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            if (await PingBrokerAsync(cancellationToken).ConfigureAwait(false)) return;
        }
    }

    private static async Task<bool> PingBrokerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.DefaultPipeName(), PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await PipeProtocol.WriteAsync(pipe, new PipeFrame(1, MessageType.PingRequest, Array.Empty<byte>()), timeout.Token).ConfigureAwait(false);
            var response = await PipeProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
            return response.Type == MessageType.PingResponse;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (IOException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private bool IsAutoStartRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return !string.IsNullOrWhiteSpace(value) && string.Equals(value.Trim().Trim('"'), BrokerPath, StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(RunValueName, Quote(BrokerPath), RegistryValueKind.String);
    }

    private static void RemoveAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private void StopInstalledBroker()
    {
        foreach (var process in Process.GetProcessesByName("ShellCommand.Broker"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && string.Equals(Path.GetFullPath(path), BrokerPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException) { }
            catch (UnauthorizedAccessException) { }
            finally { process.Dispose(); }
        }
    }

    private static void EnsureGlobalConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GlobalConfigPath)!);
        if (!File.Exists(GlobalConfigPath))
            File.WriteAllText(GlobalConfigPath, "GlobalCommands: []\nFunctions:\n  CopyPath: false\n  EditGlobal: false\n", new UTF8Encoding(false));
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
