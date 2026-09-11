using System.Threading.Channels;
using ShellCommand.Core;
namespace ShellCommand.Broker;

public enum ResolveStatus : byte { Ok, NoCommands, InvalidRequest, Busy, InternalError, UnsupportedVersion }
public sealed record MenuDto(byte Kind, string Title, string IconRef, Guid Token, IReadOnlyList<MenuDto>? Items = null);
public sealed record ResolveResult(ResolveStatus Status, IReadOnlyList<MenuDto> Items, IReadOnlyList<Diagnostic> Diagnostics);
public interface IBrokerEndpoint
{
    ResolveResult Resolve(MenuContext context);
    Task<TokenStatus> InvokeAsync(Guid token, CancellationToken cancellationToken = default);
    void Refresh(string? directory);
}
public sealed class BrokerEngine : IBrokerEndpoint, IDisposable
{
    private readonly SnapshotRuntime _runtime;
    private readonly ActionTokenStore _tokens;
    private readonly IActionExecutor _executor;
    private readonly Channel<LaunchPlan> _actions = Channel.CreateBounded<LaunchPlan>(32);
    private readonly CancellationTokenSource _stop = new();
    public BrokerEngine(SnapshotRuntime runtime, ActionTokenStore tokens, IActionExecutor executor)
    {
        _runtime = runtime; _tokens = tokens; _executor = executor;
        _ = Task.Run(ExecuteQueueAsync);
    }
    public ResolveResult Resolve(MenuContext context)
    {
        if (context.Selection.Count > 256 || context.Directory is not null && !Path.IsPathFullyQualified(context.Directory)
            || context.Selection.Any(i => !Path.IsPathFullyQualified(i.Path))) return new(ResolveStatus.InvalidRequest, [], []);
        var resolved = _runtime.Resolve(context);
        MenuDto Map(ResolvedItem item) => item.Kind switch
        {
            "separator" => new(1, "", "", Guid.Empty),
            "group" => new(2, item.Title, item.IconRef, Guid.Empty, item.Items!.Select(Map).ToArray()),
            _ => new(0, item.Title, item.IconRef, _tokens.Issue(item.Plan!))
        };
        return new(ResolveStatus.Ok, resolved.Items.Select(Map).ToArray(), resolved.Diagnostics);
    }
    public Task<TokenStatus> InvokeAsync(Guid token, CancellationToken cancellationToken = default)
    {
        // A token is consumed at most once. Lost ACKs cannot execute it twice.
        if (!_tokens.TryConsume(token, out var plan, out var status)) return Task.FromResult(status);
        return Task.FromResult(_actions.Writer.TryWrite(plan!) ? TokenStatus.Accepted : TokenStatus.Busy);
    }
    public void Refresh(string? directory) => _runtime.Refresh(directory);
    private async Task ExecuteQueueAsync()
    {
        try
        {
            await foreach (var plan in _actions.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try { await _executor.ExecuteAsync(plan, _stop.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { _stop.Cancel(); _actions.Writer.TryComplete(); _runtime.Dispose(); }
}
