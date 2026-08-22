using ShellCommand.Core;

namespace ShellCommand.Broker;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var pipeName = PipeProtocol.DefaultPipeName();
        var global = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "config", "global.shellcommand.yaml");
        var app = Path.Combine(AppContext.BaseDirectory, "ShellCommand.exe");
        var runtime = new FileConfigRuntime(globalPath: global);
        var capabilities = new BuiltInCapabilities(true, true);
        var engine = new BrokerEngine(runtime, new ActionTokenStore(), new ProcessActionExecutor(app, global), capabilities);
        using var server = new PipeServer(pipeName, engine);
        Console.WriteLine($"ShellCommand Broker listening on {pipeName}");
        await server.RunAsync(CancellationToken.None);
    }
}
