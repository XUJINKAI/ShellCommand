using System.Diagnostics;
using System.IO;

namespace ShellCommand.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow() => InitializeComponent();

    private void OpenGlobalConfig_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "config", "global.shellcommand.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "GlobalCommands: []\nFunctions:\n  CopyPath: false\n  EditGlobal: false\n");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenConfigFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "config");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
