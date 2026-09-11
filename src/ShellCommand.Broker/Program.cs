using ShellCommand.Core;

namespace ShellCommand.Broker;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--p0-probe")
        {
            using var probe = new PipeServer(args[1], new ProbeEndpoint());
            await probe.RunAsync(CancellationToken.None);
            return;
        }
        var pipeName = PipeProtocol.DefaultPipeName();
        using var instance = new Mutex(true, pipeName + ".Broker", out var created);
        if (!created) return;
        var global = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommand11", "config", "global.shellcommand.yaml");
        var app = Path.Combine(AppContext.BaseDirectory, "ShellCommand.exe");
        var runtime = new FileConfigRuntime(globalPath: global);
        var capabilities = new BuiltInCapabilities(true, true);
        var engine = new BrokerEngine(runtime, new ActionTokenStore(), new ProcessActionExecutor(app, global), capabilities);
        using var server = new PipeServer(pipeName, engine);
        Console.WriteLine($"ShellCommand Broker listening on {pipeName}");
        await server.RunAsync(CancellationToken.None);
    }
    private sealed class ProbeEndpoint : IBrokerEndpoint
    {
        public ResolveResult Resolve(string? workingDirectory) => new(ResolveStatus.Ok,
            [new(0, "Probe A", "", Guid.Parse("00000000-0000-0000-0000-000000000001")),
             new(0, "Probe B", "", Guid.Parse("00000000-0000-0000-0000-000000000002"))], []);
        public Task<TokenStatus> InvokeAsync(Guid token, CancellationToken cancellationToken = default)
            => Task.FromResult(TokenStatus.TokenNotFound); // Probe never executes anything.
    }
}
