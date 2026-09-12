using System.Text.Json;
using System.Windows;
using ShellCommand.Broker;

namespace ShellCommand.App;

public static class EntryPoint
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Emergency operations run before WPF Application construction and never load YAML.
        if (args.Contains("--uninstall", StringComparer.Ordinal) || args.Contains("--disable-integration", StringComparer.Ordinal))
        {
            var result = InstallationManager.UninstallAsync().GetAwaiter().GetResult();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        if (args.Contains("--install", StringComparer.Ordinal) || args.Contains("--developer-install", StringComparer.Ordinal))
        {
            var result = new InstallationManager().InstallOrRepairAsync().GetAwaiter().GetResult();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        if (args.Contains("--check-installation", StringComparer.Ordinal))
        {
            try { return new InstallationManager().GetStatusAsync().GetAwaiter().GetResult().IsInstalled ? 0 : 1; }
            catch (Exception) { return 1; }
        }
        var application = new App();
        application.InitializeComponent();
        if (args.Contains("--execute", StringComparer.Ordinal))
        {
            ExecutionRequest request;
            try
            {
                using var inputReader = new System.IO.StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false, true));
                var json = SnapshotRuntime.ReadBoundedAsync(inputReader, 2 * 1024 * 1024, CancellationToken.None).GetAwaiter().GetResult();
                request = JsonSerializer.Deserialize<ExecutionRequest>(json) ?? throw new InvalidOperationException("执行请求为空。");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            if (request.Plan.Actions.Any(a => a.Kind == "settings"))
            {
                TaskJournal.Save(new(request.Id, request.Plan.Title, "launched", DateTimeOffset.UtcNow));
                return application.Run(new MainWindow());
            }
            if (request.Plan.Actions.Any(a => a.Output == "window")) return application.Run(new TaskWindow(request));
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            application.Startup += async (_, _) =>
            {
                var result = await new TaskRunner().RunAsync(request, _ => { });
                if (result.Status == "failed") MessageBox.Show(result.Message, request.Plan.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                application.Shutdown(result.Status == "failed" ? 1 : 0);
            };
            return application.Run();
        }
        return application.Run(new MainWindow());
    }
}
