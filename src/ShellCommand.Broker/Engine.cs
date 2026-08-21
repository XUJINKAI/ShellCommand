using ShellCommand.Core;

namespace ShellCommand.Broker;

public enum ResolveStatus : byte { Ok, NoCommands, InvalidRequest, Busy, InternalError, UnsupportedVersion }

public sealed record MenuDto(byte Kind, string Title, string IconRef, Guid Token);
public sealed record ResolveResult(ResolveStatus Status, IReadOnlyList<MenuDto> Items, IReadOnlyList<Diagnostic> Diagnostics);

public sealed class BrokerEngine
{
    private readonly FileConfigRuntime _runtime;
    private readonly ActionTokenStore _tokens;
    private readonly IActionExecutor _executor;
    private readonly BuiltInCapabilities _capabilities;

    public BrokerEngine(FileConfigRuntime runtime, ActionTokenStore tokens, IActionExecutor executor, BuiltInCapabilities? capabilities = null)
    {
        _runtime = runtime;
        _tokens = tokens;
        _executor = executor;
        _capabilities = capabilities ?? new BuiltInCapabilities(false, false);
    }

    public ResolveResult Resolve(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathFullyQualified(workingDirectory))
            return new(ResolveStatus.InvalidRequest, Array.Empty<MenuDto>(), Array.Empty<Diagnostic>());
        try
        {
            var directory = Path.GetFullPath(workingDirectory);
            if (!Directory.Exists(directory)) return new(ResolveStatus.InvalidRequest, Array.Empty<MenuDto>(), Array.Empty<Diagnostic>());
            var snapshot = _runtime.Load(directory);
            var menu = MenuResolver.Resolve(snapshot.Global, snapshot.Directory, new FileSystemDirectoryFacts(directory), directory, _capabilities);
            var dtos = new List<MenuDto>(menu.Items.Count);
            foreach (var item in menu.Items.Take(100))
            {
                if (item is ResolvedItem.Separator) dtos.Add(new(1, string.Empty, string.Empty, Guid.Empty));
                else if (item is ResolvedItem.Action action) dtos.Add(new(0, action.Title, action.IconRef ?? string.Empty, _tokens.Issue(action.Spec)));
            }
            return new(dtos.Count == 0 ? ResolveStatus.NoCommands : ResolveStatus.Ok, dtos, snapshot.Diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(ResolveStatus.InternalError, Array.Empty<MenuDto>(), new[] { new Diagnostic(string.Empty, DiagnosticSeverity.Error, "RESOLVE_FAILED", ex.Message) });
        }
    }

    public async Task<TokenStatus> InvokeAsync(Guid token, CancellationToken cancellationToken = default)
    {
        if (!_tokens.TryConsume(token, out var spec, out var status)) return status;
        try
        {
            await _executor.ExecuteAsync(spec!, cancellationToken).ConfigureAwait(false);
            return TokenStatus.Accepted;
        }
        catch (OperationCanceledException) { return TokenStatus.InternalError; }
        catch (Exception) { return TokenStatus.InternalError; }
    }
}
