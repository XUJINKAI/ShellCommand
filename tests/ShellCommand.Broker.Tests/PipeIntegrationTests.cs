using System.Diagnostics;
using System.IO.Pipes;
using ShellCommand.Broker;

namespace ShellCommand.Broker.Tests;

public sealed class PipeIntegrationTests
{
    [Fact]
    public async Task SeparateBrokerProcessAcceptsFramesAndReclaimsStalledConnection()
    {
        var name = "ShellCommand11.Test." + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--p0-probe");
        start.ArgumentList.Add(name);
        using var broker = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(timeout.Token);
                await PipeProtocol.WriteAsync(pipe, new(52, MessageType.ResolveMenuRequest, PipeServer.ContextPayload(new(@"C:\Test", []))), timeout.Token);
                var response = await PipeProtocol.ReadAsync(pipe, timeout.Token);
                Assert.Equal((uint)52, response.RequestId);
                Assert.Equal(MessageType.ResolveMenuResponse, response.Type);
                Assert.Equal(new byte[] { 0, 2, 0 }, response.Payload.Take(3));
            }
            using (var stalled = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await stalled.ConnectAsync(timeout.Token);
                await stalled.WriteAsync("SC"u8.ToArray(), timeout.Token);
                var one = new byte[1];
                Assert.Equal(0, await stalled.ReadAsync(one, timeout.Token));
            }
            using var healthy = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await healthy.ConnectAsync(timeout.Token);
            await PipeProtocol.WriteAsync(healthy, new(53, MessageType.PingRequest, []), timeout.Token);
            var ping = await PipeProtocol.ReadAsync(healthy, timeout.Token);
            Assert.Equal((uint)53, ping.RequestId);
            Assert.Equal(MessageType.PingResponse, ping.Type);
        }
        finally
        {
            if (!broker.HasExited) broker.Kill(true);
            await broker.WaitForExitAsync();
        }
    }
}
