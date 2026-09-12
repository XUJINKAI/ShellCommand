using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShellCommand.Broker;
namespace ShellCommand.App;

public sealed class TaskWindow : Window
{
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, Background = new SolidColorBrush(Color.FromRgb(23, 29, 42)), Foreground = Brushes.White, Padding = new Thickness(16) };
    private readonly TextBlock _status = new() { Text = "准备运行…", Margin = new Thickness(0, 8, 0, 12) };
    private readonly TaskRunner _runner = new();
    private bool _running = true;
    private bool _closed;
    public TaskWindow(ExecutionRequest request)
    {
        Title = request.Plan.Title; Width = 820; Height = 540; MinWidth = 500; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(24) };
        var title = new TextBlock { Text = request.Plan.Title, FontSize = 24, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title); DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Content = "复制输出" }; copy.Click += (_, _) => Clipboard.SetText(_output.Text);
        var stop = new Button { Content = "停止", Margin = new Thickness(8, 0, 0, 0) }; stop.Click += (_, _) => _runner.Stop();
        var close = new Button { Content = "关闭", Margin = new Thickness(8, 0, 0, 0) }; close.Click += (_, _) => Close();
        buttons.Children.Add(copy); buttons.Children.Add(stop); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(_output); Content = root;
        Closing += (_, e) => { if (_running) { e.Cancel = true; _closed = true; Hide(); } };
        Loaded += async (_, _) =>
        {
            _status.Text = "运行中 · 关闭窗口后继续运行，可点击停止终止任务";
            var result = await _runner.RunAsync(request, text =>
            {
                _output.AppendText(text);
                if (_output.Text.Length > 262144) _output.Text = "[较早的输出已截断]\n" + _output.Text[^196608..];
                _output.ScrollToEnd();
            });
            _running = false; stop.IsEnabled = false; _status.Text = result.Message ?? result.Status;
            if (_closed) Application.Current.Shutdown();
        };
    }
}
