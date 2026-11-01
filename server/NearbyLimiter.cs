using System.Collections.Concurrent;

namespace DotWatcher.Server;

/// <summary>
/// Caps how often one signed-in user can ask for the nearby list or try to join a nearby session. The
/// list takes a client-supplied position, so without a cap someone could walk it across a map to find
/// organisers; the cap, the small radius, the rounded distance and the result limit are what make that
/// costly (see .ai/plans/nearby-join.md). Fixed one-hour windows, in memory, per server process (like
/// <see cref="CrewCodeLimiter"/>). Every attempt counts, whether or not it succeeded.
/// </summary>
public sealed class NearbyLimiter(TimeProvider timeProvider)
{
    /// <summary>Clients refresh about once a minute while the screen is open, so this leaves room for retries.</summary>
    public const int ListsPerUserPerHour = 120;

    public const int JoinsPerUserPerHour = 20;

    private const int MaxTrackedUsers = 10_000;

    private readonly ConcurrentDictionary<string, Window> _lists = new();
    private readonly ConcurrentDictionary<string, Window> _joins = new();
    private readonly object _lock = new();

    public bool TryAcceptList(string userId) => TryAccept(_lists, userId, ListsPerUserPerHour);

    public bool TryAcceptJoin(string userId) => TryAccept(_joins, userId, JoinsPerUserPerHour);

    private bool TryAccept(ConcurrentDictionary<string, Window> users, string userId, int perHour)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (users.Count >= MaxTrackedUsers && !users.ContainsKey(userId))
                users.Clear(); // a flood of accounts can't grow this without bound

            var window = users.GetOrAdd(userId, _ => new Window(now, 0));
            if (now - window.Start >= TimeSpan.FromHours(1))
                window = new Window(now, 0);
            if (window.Count >= perHour)
            {
                users[userId] = window;
                return false;
            }

            users[userId] = window with { Count = window.Count + 1 };
            return true;
        }
    }

    private sealed record Window(DateTimeOffset Start, int Count);
}
