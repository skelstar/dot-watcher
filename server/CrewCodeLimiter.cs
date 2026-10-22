using System.Collections.Concurrent;

namespace DotWatcher.Server;

/// <summary>
/// Caps how many crew-code previews and redemptions one signed-in user can attempt, so a crew code
/// can't be guessed by brute force. Fixed one-hour windows, in memory, per server process (like
/// <see cref="ClientErrorLimiter"/>). Every attempt counts, whether or not the code was valid.
/// </summary>
public sealed class CrewCodeLimiter(TimeProvider timeProvider)
{
    private const int PerUserPerHour = 20;
    private const int MaxTrackedUsers = 10_000;

    private readonly ConcurrentDictionary<string, Window> _users = new();
    private readonly object _lock = new();

    public bool TryAccept(string userId)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_users.Count >= MaxTrackedUsers && !_users.ContainsKey(userId))
                _users.Clear(); // a flood of accounts can't grow this without bound

            var window = _users.GetOrAdd(userId, _ => new Window(now, 0));
            if (now - window.Start >= TimeSpan.FromHours(1))
                window = new Window(now, 0);
            if (window.Count >= PerUserPerHour)
            {
                _users[userId] = window;
                return false;
            }

            _users[userId] = window with { Count = window.Count + 1 };
            return true;
        }
    }

    private sealed record Window(DateTimeOffset Start, int Count);
}
