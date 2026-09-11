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
            var result = new InstallationManager().InstallOrRepairAsync(developerRegistration: args.Contains("--developer-install", StringComparer.Ordinal)).GetAwaiter().GetResult();
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }
        if (args.Contains("--check-installation", StringComparer.Ordinal))
        {
            try { return new InstallationManager().GetStatusAsync().GetAwaiter().GetResult().IsInstalled ? 0 : 1; }
            catch (Exception) { return 1; }
        }
        var application = new App();
        return application.Run();
    }
}
