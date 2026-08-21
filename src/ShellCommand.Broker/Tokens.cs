using System.Security.Cryptography;
using ShellCommand.Core;

namespace ShellCommand.Broker;

public enum TokenStatus { Accepted, TokenNotFound, TokenExpired, Busy, InternalError }

public sealed class ActionTokenStore
{
    private sealed record Entry(ActionSpec Spec, DateTimeOffset ExpiresAt);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly TimeSpan _ttl;

    public ActionTokenStore(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromMinutes(2);

    public Guid Issue(ActionSpec spec)
    {
        var token = Guid.NewGuid();
        lock (_gate) _entries[token] = new Entry(spec, DateTimeOffset.UtcNow + _ttl);
        return token;
    }

    public bool TryConsume(Guid token, out ActionSpec? spec, out TokenStatus status)
    {
        lock (_gate)
        {
            if (!_entries.Remove(token, out var entry)) { spec = null; status = TokenStatus.TokenNotFound; return false; }
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow) { spec = null; status = TokenStatus.TokenExpired; return false; }
            spec = entry.Spec;
            status = TokenStatus.Accepted;
            return true;
        }
    }
}
