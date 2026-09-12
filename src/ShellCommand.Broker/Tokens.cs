using ShellCommand.Core;
namespace ShellCommand.Broker;

public enum TokenStatus { Accepted, TokenNotFound, TokenExpired, Busy, InternalError }
public sealed class ActionTokenStore
{
    private sealed record Entry(LaunchPlan Plan, DateTimeOffset Expires, int Characters);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private int _characters;
    public ActionTokenStore(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromMinutes(2);
    public Guid Issue(LaunchPlan plan)
    {
        lock (_gate)
        {
            var characters = PlanLimits.Characters(plan);
            if (characters > PlanLimits.MaxCharacters) throw new InvalidOperationException("执行计划过大。");
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _entries.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) Remove(key);
            while (_entries.Count >= 4096 || _characters + characters > 16 * 1024 * 1024) Remove(_entries.MinBy(p => p.Value.Expires).Key);
            var token = Guid.NewGuid(); _entries[token] = new(plan, now + _ttl, characters); _characters += characters; return token;
        }
    }
    public bool TryConsume(Guid token, out LaunchPlan? plan, out TokenStatus status)
    {
        lock (_gate)
        {
            plan = null;
            if (!_entries.Remove(token, out var entry)) { status = TokenStatus.TokenNotFound; return false; }
            _characters -= entry.Characters;
            if (entry.Expires <= DateTimeOffset.UtcNow) { status = TokenStatus.TokenExpired; return false; }
            plan = entry.Plan; status = TokenStatus.Accepted; return true;
        }
    }
    private void Remove(Guid key) { if (_entries.Remove(key, out var entry)) _characters -= entry.Characters; }
    public int Count { get { lock (_gate) return _entries.Count; } }
}
