using System.Diagnostics;
using System.IO;

namespace ShellCommand.App;

public partial class MainWindow : System.Windows.Window
{
    private readonly InstallationManager _installation = new();
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        if (_busy) return;
        try
        {
            var status = await _installation.GetStatusAsync();
            StatusTitle.Text = status.State switch
            {
                IntegrationState.Installed => "已安装",
                IntegrationState.NeedsRepair => "需要修复",
                IntegrationState.PackageFilesMissing => "安装文件不完整",
                _ => "尚未安装"
            };
            StatusMessage.Text = status.Message;
            PackageStatus.Text = status.PackageRegistered ? "已注册" : "未注册";
            BrokerStatus.Text = status.BrokerRunning ? "运行中" : "未运行";
            AutoStartStatus.Text = status.AutoStartRegistered ? "已启用" : "未启用";
            FilesStatus.Text = status.FilesPresent ? "完整" : "缺少文件";
            InstallButton.Content = status.State == IntegrationState.NotInstalled ? "安装" : "安装 / 修复";
            UninstallButton.IsEnabled = status.PackageRegistered || status.AutoStartRegistered;
        }
        catch (Exception ex)
        {
            StatusTitle.Text = "无法读取状态";
            StatusMessage.Text = ex.Message;
        }
    }

    private async void Refresh_Click(object sender, System.Windows.RoutedEventArgs e) => await RefreshStatusAsync();

    private async void Install_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtonsEnabled(false);
        try
        {
            var result = await _installation.InstallOrRepairAsync();
            System.Windows.MessageBox.Show(this, result.Message, result.Success ? "安装完成" : "安装失败", System.Windows.MessageBoxButton.OK, result.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
            if (result.Success && AskRestartExplorer()) InstallationManager.RestartExplorer();
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
            await RefreshStatusAsync();
        }
    }

    private async void Uninstall_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_busy) return;
        var answer = System.Windows.MessageBox.Show(this, "这会移除 ShellCommand 的右键菜单集成和开机启动，但会保留你的配置文件。继续吗？", "确认卸载", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        _busy = true;
        SetButtonsEnabled(false);
        try
        {
            var result = await _installation.UninstallAsync();
            System.Windows.MessageBox.Show(this, result.Message, result.Success ? "卸载完成" : "卸载失败", System.Windows.MessageBoxButton.OK, result.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
            if (result.Success && AskRestartExplorer()) InstallationManager.RestartExplorer();
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
            await RefreshStatusAsync();
        }
    }

    private void RestartExplorer_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(this, "Explorer 会短暂消失并自动重新启动。继续吗？", "重启 Explorer", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer == System.Windows.MessageBoxResult.Yes) InstallationManager.RestartExplorer();
    }

    private bool AskRestartExplorer()
        => System.Windows.MessageBox.Show(this, "右键菜单状态可能需要重启 Explorer 才会刷新。现在重启吗？", "刷新右键菜单", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;

    private void SetButtonsEnabled(bool enabled)
    {
        InstallButton.IsEnabled = enabled;
        UninstallButton.IsEnabled = enabled;
        RestartButton.IsEnabled = enabled;
    }

    private void OpenGlobalConfig_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstallationManager.GlobalConfigPath)!);
        if (!File.Exists(InstallationManager.GlobalConfigPath))
            File.WriteAllText(InstallationManager.GlobalConfigPath, "GlobalCommands: []\nFunctions:\n  CopyPath: false\n  EditGlobal: false\n");
        Process.Start(new ProcessStartInfo(InstallationManager.GlobalConfigPath) { UseShellExecute = true });
    }

    private void OpenConfigFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(InstallationManager.GlobalConfigPath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
