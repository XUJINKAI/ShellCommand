using System.Text.Json;
using ShellCommand.Core;
namespace ShellCommand.Broker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--prepare")
        {
            try
            {
                using var inputReader = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false, true));
                var input = await SnapshotRuntime.ReadBoundedAsync(inputReader, 2 * 1024 * 1024, CancellationToken.None);
                var request = JsonSerializer.Deserialize<PrepareRequest>(input) ?? throw new InvalidDataException("缺少准备请求。");
                Console.Write(JsonSerializer.Serialize(Preparation.Prepare(request))); return 0;
            }
            catch (Exception ex) { Console.Error.Write(ex.Message); return 1; }
        }
        if (args.Length == 2 && args[0] == "--p0-probe")
        {
            using var probe = new PipeServer(args[1], new ProbeEndpoint());
            await probe.RunAsync(CancellationToken.None); return 0;
        }
        var pipeName = PipeProtocol.DefaultPipeName();
        using var instance = new Mutex(true, pipeName + ".Broker", out var created);
        if (!created) return 0;
        using var runtime = new SnapshotRuntime();
        using var engine = new BrokerEngine(runtime, new(), new ProcessActionExecutor(Path.Combine(AppContext.BaseDirectory, "ShellCommand.exe")));
        using var server = new PipeServer(pipeName, engine);
        await server.RunAsync(CancellationToken.None); return 0;
    }
    private sealed class ProbeEndpoint : IBrokerEndpoint
    {
        public ResolveResult Resolve(MenuContext context) => new(ResolveStatus.Ok,
            [new(0, "Probe A", "", Guid.Parse("00000000-0000-0000-0000-000000000001")),
             new(0, "Probe B", "", Guid.Parse("00000000-0000-0000-0000-000000000002"))], []);
        public Task<TokenStatus> InvokeAsync(Guid token, CancellationToken cancellationToken = default) => Task.FromResult(TokenStatus.TokenNotFound);
        public void Refresh(string? directory) { }
    }
}
