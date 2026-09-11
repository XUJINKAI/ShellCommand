using ShellCommand.Core;
namespace ShellCommand.Broker;

public enum TokenStatus { Accepted, TokenNotFound, TokenExpired, Busy, InternalError }
public sealed class ActionTokenStore
{
    private sealed record Entry(LaunchPlan Plan, DateTimeOffset Expires);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    public ActionTokenStore(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromMinutes(2);
    public Guid Issue(LaunchPlan plan)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _entries.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) _entries.Remove(key);
            if (_entries.Count >= 4096) _entries.Remove(_entries.MinBy(p => p.Value.Expires).Key);
            var token = Guid.NewGuid(); _entries[token] = new(plan, now + _ttl); return token;
        }
    }
    public bool TryConsume(Guid token, out LaunchPlan? plan, out TokenStatus status)
    {
        lock (_gate)
        {
            plan = null;
            if (!_entries.Remove(token, out var entry)) { status = TokenStatus.TokenNotFound; return false; }
            if (entry.Expires <= DateTimeOffset.UtcNow) { status = TokenStatus.TokenExpired; return false; }
            plan = entry.Plan; status = TokenStatus.Accepted; return true;
        }
    }
    public int Count { get { lock (_gate) return _entries.Count; } }
}
