using System.Collections.Concurrent;

namespace DotWatcher.Server;

/// <summary>
/// Slows down guessing of watch links. A real link is 128 bits, so guessing can't work; this just keeps
/// a flood of wrong guesses from becoming a flood of database lookups. Only a lookup that finds nothing
/// is counted, and a request with a valid link is never refused, so someone hammering wrong guesses
/// can't lock real watchers out (behind the front proxy every caller can look like one address).
/// Fixed one-minute windows, in memory, per server process.
/// </summary>
public sealed class WatchLinkLimiter(TimeProvider timeProvider)
{
    private const int MissesPerMinute = 120;
    private const int MaxTrackedKeys = 10_000;

    private readonly ConcurrentDictionary<string, Window> _keys = new();
    private readonly object _lock = new();

    /// <summary>True when this caller has used up its wrong guesses for the minute.</summary>
    public bool IsLimited(string key)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            return _keys.TryGetValue(key, out var window)
                && now - window.Start < TimeSpan.FromMinutes(1)
                && window.Count >= MissesPerMinute;
        }
    }

    public void RecordMiss(string key)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_keys.Count >= MaxTrackedKeys && !_keys.ContainsKey(key))
                _keys.Clear();

            var window = _keys.GetOrAdd(key, _ => new Window(now, 0));
            if (now - window.Start >= TimeSpan.FromMinutes(1))
                window = new Window(now, 0);
            _keys[key] = window with { Count = window.Count + 1 };
        }
    }

    private sealed record Window(DateTimeOffset Start, int Count);
}
