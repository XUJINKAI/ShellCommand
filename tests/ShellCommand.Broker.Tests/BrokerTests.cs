using ShellCommand.Broker;
using ShellCommand.Core;
namespace ShellCommand.Broker.Tests;
public sealed class BrokerTests
{
    private static LaunchPlan Plan => new("Test", "", [new("copy", Text: "hello")]);
    [Fact] public void TokensAreSingleUseAndExpire()
    {
        var store = new ActionTokenStore(TimeSpan.Zero); var token = store.Issue(Plan);
        Assert.False(store.TryConsume(token, out _, out var status)); Assert.Equal(TokenStatus.TokenExpired, status);
        Assert.False(store.TryConsume(token, out _, out status)); Assert.Equal(TokenStatus.TokenNotFound, status);
    }
    [Fact] public void UnclickedTokensRemainBounded()
    {
        var store = new ActionTokenStore();
        for (var i = 0; i < 10000; i++) store.Issue(Plan);
        Assert.Equal(4096, store.Count);
    }
    [Fact] public async Task InvokeAcknowledgesBeforeExecutorCompletes()
    {
        using var runtime = new SnapshotRuntime(appDirectory: Path.GetTempPath());
        var tokens = new ActionTokenStore(); var executor = new WaitingExecutor();
        using var engine = new BrokerEngine(runtime, tokens, executor);
        var token = tokens.Issue(Plan);
        Assert.Equal(TokenStatus.Accepted, await engine.InvokeAsync(token).WaitAsync(TimeSpan.FromSeconds(1)));
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(TokenStatus.TokenNotFound, await engine.InvokeAsync(token));
    }
    private sealed class WaitingExecutor : IActionExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExecuteAsync(LaunchPlan plan, CancellationToken cancellationToken) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
    }
    [Fact] public async Task PipeFrameRejectsLargePayload()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeProtocol.WriteAsync(stream, new(1, MessageType.PingRequest, new byte[PipeProtocol.MaxPayload + 1]), default));
    }
    [Fact] public void ContextRoundTripsAllSelectionsAndAbsentDirectory()
    {
        var context = new MenuContext(null, [new(@"C:\中文\one.txt", false), new(@"D:\folder", true)]);
        var decoded = PipeServer.DecodeContext(PipeServer.ContextPayload(context));
        Assert.Null(decoded.Directory); Assert.Equal(context.Selection, decoded.Selection);
        Assert.Throws<InvalidDataException>(() => PipeServer.DecodeContext(PipeServer.ContextPayload(context).Concat(new byte[] { 1 }).ToArray()));
    }
    [Fact] public void NestedMenuWireRetainsChildren()
    {
        var response = PipeServer.EncodeResolve(new(ResolveStatus.Ok, [new(2, "Group", "", Guid.Empty, [new(0, "Child", "", Guid.NewGuid())])], []));
        Assert.Equal(0, response[0]); Assert.True(response.Length > 60);
    }
}
