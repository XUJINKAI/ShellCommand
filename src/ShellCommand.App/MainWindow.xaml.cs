using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ShellCommand.Broker;
using ShellCommand.Config.Yaml;
using ShellCommand.Core;
namespace ShellCommand.App;

public partial class MainWindow : Window
{
    private readonly InstallationManager _installation = new();
    private readonly ContextMenuManager _menus = new();
    private string _source = InstallationManager.GlobalConfigPath;
    private string? _baseline;
    private bool _busy;
    private readonly List<SelectionItem> _selection = new();
    public MainWindow()
    {
        InitializeComponent();
        PreviewDirectory.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Loaded += async (_, _) => await RunUi(async () => { await LoadEditor(); await RefreshStatus(); await Preview(); await LoadTasks(); });
        Closing += (_, e) => { if (Dirty() && MessageBox.Show(this, "还有未保存的修改，直接关闭？", "ShellCommand", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true; };
    }
    private bool Dirty() => Editor.Text != (_baseline ?? DefaultConfiguration.Text);
    private async Task RunUi(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; Notice.Text = "处理中…";
        try { await action(); }
        catch (Exception ex) { Notice.Text = ex is OperationCanceledException ? "操作超时，请重试。" : ex.Message; }
        finally { _busy = false; }
    }
    private async Task LoadEditor()
    {
        _baseline = await Task.Run(() => File.Exists(_source) ? Preparation.ReadText(_source, ConfigParser.MaxFileBytes) : null);
        Editor.Text = _baseline ?? DefaultConfiguration.Text; SourceLabel.Text = _source; SourceLabel.ToolTip = _source;
        Notice.Text = "已载入 · " + _source;
    }
    private void Editor_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) Notice.Text = Dirty() ? "有未保存的修改" : "已保存"; }
    private async Task ChangeSource(string source)
    {
        if (Dirty() && MessageBox.Show(this, "切换配置会放弃未保存的修改，继续？", "ShellCommand", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _source = source; await LoadEditor(); await Preview();
    }
    private async void Global_Click(object sender, RoutedEventArgs e) => await RunUi(() => ChangeSource(InstallationManager.GlobalConfigPath));
    private async void Local_Click(object sender, RoutedEventArgs e) => await RunUi(async () =>
    {
        if (string.IsNullOrWhiteSpace(PreviewDirectory.Text)) throw new InvalidOperationException("先选择一个预览目录。");
        await ChangeSource(Path.Combine(Path.GetFullPath(PreviewDirectory.Text), ".shellcommand.yaml"));
    });
    private async void Reload_Click(object sender, RoutedEventArgs e) => await RunUi(async () =>
    {
        if (!Dirty() || MessageBox.Show(this, "放弃编辑内容，重新读取文件？", "ShellCommand", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { await LoadEditor(); await Preview(); }
    });
    private async void Save_Click(object sender, RoutedEventArgs e) => await RunUi(async () =>
    {
        var parsed = ConfigParser.Parse(Editor.Text, _source);
        if (!parsed.IsValid) { ShowDiagnostics(parsed.Diagnostics); Notice.Text = "配置有错误，尚未保存。"; return; }
        var text = Editor.Text; var baseline = _baseline; var source = _source;
        await Task.Run(() =>
        {
            var actual = File.Exists(source) ? Preparation.ReadText(source, ConfigParser.MaxFileBytes) : null;
            if (actual != baseline) throw new InvalidOperationException("文件已被外部修改，请重新载入后合并，未覆盖外部内容。");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            var temporary = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); File.Move(temporary, source, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
        _baseline = text;
        await NotifyBroker(); await Preview(); Notice.Text = "已保存 · 下一次打开菜单时生效";
    });
    private async Task NotifyBroker()
    {
        try
        {
            using var timeout = new CancellationTokenSource(500);
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.DefaultPipeName(), PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            await PipeProtocol.WriteAsync(pipe, new(77, MessageType.RefreshRequest, PipeProtocol.StringPayload(PreviewDirectory.Text)), timeout.Token);
            await PipeProtocol.ReadAsync(pipe, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }
    private async Task Preview()
    {
        var directory = string.IsNullOrWhiteSpace(PreviewDirectory.Text) ? null : Path.GetFullPath(PreviewDirectory.Text);
        var context = new MenuContext(directory, _selection.ToArray());
        var request = new PrepareRequest(directory, DeploymentPackage.DataRoot, _installation.AppRoot, _source, Editor.Text);
        var snapshot = await SnapshotRuntime.PrepareAsync(request, Path.Combine(_installation.AppRoot, "ShellCommand.Broker.exe"), CancellationToken.None);
        var env = new ResolveEnvironment(_installation.AppRoot, DeploymentPackage.DataRoot, Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().ToDictionary(p => (string)p.Key, p => (string)p.Value!, StringComparer.OrdinalIgnoreCase));
        var menu = MenuResolver.Resolve(snapshot.Global.Config, snapshot.Local.Config, snapshot.Facts, context, env);
        PreviewTree.Items.Clear();
        TreeViewItem Map(ResolvedItem item)
        {
            var view = new TreeViewItem { Header = item.Kind == "separator" ? "────────" : item.Title, Tag = item, IsExpanded = true, Padding = new Thickness(4, 5, 4, 5) };
            foreach (var child in item.Items ?? []) view.Items.Add(Map(child));
            return view;
        }
        foreach (var item in menu.Items) PreviewTree.Items.Add(Map(item));
        ShowDiagnostics(snapshot.Global.Diagnostics.Concat(snapshot.Local.Diagnostics).Concat(menu.Diagnostics));
        Notice.Text = _selection.Count == 0 ? "背景菜单预览 · 不会执行命令" : $"已选择 {_selection.Count} 项 · 预览不会执行命令";
    }
    private void ShowDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        var values = diagnostics.ToArray();
        ValidationTitle.Text = values.Any(d => d.Severity == DiagnosticSeverity.Error) ? "配置错误 · 上次有效菜单仍可使用" : "诊断与隐藏原因";
        DiagnosticsList.ItemsSource = values.Length == 0 ? new[] { "配置有效，没有诊断。" } : values.Select(d => $"{d.Severity} · {Path.GetFileName(d.SourcePath)}:{d.Line}:{d.Column} {d.Field} · {d.Message}").ToArray();
    }
    private async void Preview_Click(object sender, RoutedEventArgs e) => await RunUi(Preview);
    private void Preview_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: ResolvedItem { Plan: { } plan } }) PlanDetails.Text = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        else PlanDetails.Text = "选择一个动作，查看展开后的实际参数。";
    }
    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择背景目录" };
        if (dialog.ShowDialog(this) == true) { PreviewDirectory.Text = dialog.FolderName; _selection.Clear(); }
    }
    private void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "选择预览文件" };
        if (dialog.ShowDialog(this) != true) return;
        _selection.Clear(); _selection.AddRange(dialog.FileNames.Take(256).Select(p => new SelectionItem(p, false))); UpdateSelectionDirectory();
    }
    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = true, Title = "选择预览文件夹" };
        if (dialog.ShowDialog(this) != true) return;
        _selection.Clear(); _selection.AddRange(dialog.FolderNames.Take(256).Select(p => new SelectionItem(p, true))); UpdateSelectionDirectory();
    }
    private void UpdateSelectionDirectory()
    {
        var parents = _selection.Select(s => Path.GetDirectoryName(s.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        PreviewDirectory.Text = _selection.Count == 1 && _selection[0].IsFolder ? _selection[0].Path : parents.Length == 1 ? parents[0] ?? "" : "";
        Notice.Text = $"已选择 {_selection.Count} 项，点击预览查看菜单。";
    }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) { _selection.Clear(); Notice.Text = "已清空选择，当前为背景菜单。"; }
    private async Task RefreshStatus()
    {
        var status = await _installation.GetStatusAsync();
        StatusTitle.Text = status.State switch { IntegrationState.Installed => "已启用", IntegrationState.NotInstalled => "尚未启用", _ => "需要修复" };
        IntegrationBadge.Text = StatusTitle.Text; StatusMessage.Text = status.Message;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunUi(RefreshStatus);
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunUi(async () =>
    {
        var result = await _installation.InstallOrRepairAsync(); Notice.Text = result.Message;
        MessageBox.Show(this, result.Message, "ShellCommand"); await RefreshStatus();
    });
    private async void Uninstall_Click(object sender, RoutedEventArgs e) => await RunUi(async () =>
    {
        if (MessageBox.Show(this, "卸载菜单集成和自启动，保留配置与菜单恢复记录？", "ShellCommand", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var result = await InstallationManager.UninstallAsync(); Notice.Text = result.Message; await RefreshStatus();
    });
    private void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "重启当前会话的资源管理器？已打开的文件夹窗口会关闭。", "ShellCommand", MessageBoxButton.YesNo) == MessageBoxResult.Yes) InstallationManager.RestartExplorer();
    }
    private static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { path } })?.Dispose(); }
    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(DeploymentPackage.DataRoot, "config"));
    private void OpenStateFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(DeploymentPackage.DataRoot, "state"));
    private async Task LoadTasks()
    {
        var tasks = await Task.Run(TaskJournal.Read);
        static string State(string state) => state switch { "queued" => "等待执行", "started" => "运行中", "launched" => "已启动", "completed" => "已完成", "failed" => "失败", "canceled" => "已取消", _ => state };
        TasksList.ItemsSource = tasks.Select(t => $"{t.Created.LocalDateTime:g}   {State(t.Status)}   {t.Title}   {t.Message}").ToArray();
    }
    private async void Tasks_Click(object sender, RoutedEventArgs e) => await RunUi(LoadTasks);
    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunUi(async () => { SystemMenus.ItemsSource = await Task.Run(ContextMenuScanner.Scan); Notice.Text = "扫描完成。机器级和现代打包菜单只读。"; });
    private void SystemMenu_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (SystemMenus.SelectedItem is MenuEntry entry) SystemDetails.Text = $"{entry.Type} · {entry.Scope} · {entry.State}\n{entry.Source}\\{entry.RegistrationPath}\n{entry.Clsid}\n{entry.CommandOrDll}\n{(entry.CanModify ? "支持当前用户禁用；恢复时检查外部修改。" : "只读：没有受支持的当前用户修改方式。")}";
    }
    private async Task ChangeMenu(bool restore)
    {
        if (SystemMenus.SelectedItem is not MenuEntry entry) return;
        if (!restore && MessageBox.Show(this, "禁用此项？如果是 COM 扩展，会影响它在当前用户下的所有注册位置。", "ShellCommand", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var result = await Task.Run(() => restore ? _menus.Restore(entry) : _menus.Disable(entry)); Notice.Text = result.Message;
        SystemMenus.ItemsSource = await Task.Run(ContextMenuScanner.Scan);
    }
    private async void DisableMenu_Click(object sender, RoutedEventArgs e) => await RunUi(() => ChangeMenu(false));
    private async void RestoreMenu_Click(object sender, RoutedEventArgs e) => await RunUi(() => ChangeMenu(true));
}
