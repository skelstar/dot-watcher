using System.Security.Cryptography;

namespace DotWatcher.Server;

/// <summary>
/// "Find new crew": people standing together switch it on, see each other by name, and become crew when
/// both have accepted. Each window lasts <see cref="Window"/> and lives only in memory (per server
/// process, like <see cref="NearbyLimiter"/>): nothing about it needs to survive a restart. See
/// .ai/plans/crew-finding.md.
///
/// Privacy rules kept here: people only see others who also have it on, within <see cref="RadiusMeters"/>
/// (with the same GPS allowance as nearby join); others are named by a random token for the window,
/// never their account id; and no position or distance ever leaves the server. Blocked pairs and
/// existing crew are filtered by the caller (<see cref="Controllers.CrewController"/>), which has the
/// database.
/// </summary>
public sealed class CrewFinder(TimeProvider timeProvider)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    public const int RadiusMeters = 50;

    /// <summary>Clients refresh every few seconds; someone whose last fix is older than this isn't listed.</summary>
    public static readonly TimeSpan FixFreshFor = TimeSpan.FromSeconds(30);

    /// <summary>Turning it on, per user per hour. Each window is short, so this is generous.</summary>
    public const int StartsPerUserPerHour = 30;

    /// <summary>Refreshes per window: about one every two seconds, far more than a client sends.</summary>
    public const int RefreshesPerWindow = 60;

    public const int MaxListed = 20;

    private const int MaxTrackedUsers = 10_000;

    private readonly object _lock = new();
    private readonly Dictionary<string, Finder> _byUser = new();
    private readonly Dictionary<string, Finder> _byToken = new();
    private readonly Dictionary<string, StartWindow> _starts = new();

    public enum StartOutcome { Ok, TooMany }

    public enum RefreshOutcome { Ok, NotOn, TooMany }

    public enum AcceptOutcome
    {
        /// <summary>The caller's own window has ended.</summary>
        NotOn,
        /// <summary>No such person on now, or no longer close enough.</summary>
        NotFound,
        /// <summary>Recorded; waiting for the other person.</summary>
        Waiting,
        /// <summary>Both have now accepted: the caller should make them crew.</summary>
        Mutual,
    }

    /// <summary>A person on now, as the caller sees them: an account id for the caller's own filtering,
    /// and the token that is all the client is told.</summary>
    public record Candidate(string UserId, string Token, bool YouAccepted, bool AcceptedYou);

    /// <summary>Turns it on (or restarts it) at <paramref name="fix"/>: a new token, a new window, and any
    /// earlier accepts dropped.</summary>
    public StartOutcome Start(string userId, Fix fix)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            if (!TryCountStart(userId, now))
                return StartOutcome.TooMany;

            RemoveLocked(userId);
            var finder = new Finder(userId, NewToken(), now, fix, now);
            _byUser[userId] = finder;
            _byToken[finder.Token] = finder;
            return StartOutcome.Ok;
        }
    }

    /// <summary>A fresh position while it's on. <see cref="RefreshOutcome.NotOn"/> once the window has ended.</summary>
    public RefreshOutcome Refresh(string userId, Fix fix)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            if (!_byUser.TryGetValue(userId, out var finder))
                return RefreshOutcome.NotOn;
            if (finder.Refreshes >= RefreshesPerWindow)
                return RefreshOutcome.TooMany;

            finder.Refreshes++;
            finder.Fix = fix;
            finder.FixAt = now;
            return RefreshOutcome.Ok;
        }
    }

    /// <summary>Turns it off. Unanswered accepts, the caller's and everyone else's towards them, go with it.</summary>
    public void Stop(string userId)
    {
        lock (_lock)
            RemoveLocked(userId);
    }

    public bool IsOn(string userId)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            return _byUser.ContainsKey(userId);
        }
    }

    /// <summary>When the caller's window closes, or null when it isn't on.</summary>
    public DateTimeOffset? ExpiresAt(string userId)
    {
        lock (_lock)
            return _byUser.TryGetValue(userId, out var finder) ? finder.StartedAt + Window : null;
    }

    /// <summary>Everyone else who is on, close to the caller, and has a fresh fix; nearest is not implied
    /// by the order (the client must not learn distance), so the list is in the order people turned it on.</summary>
    public IReadOnlyList<Candidate> Nearby(string userId)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            if (!_byUser.TryGetValue(userId, out var me) || !IsFresh(me, now))
                return [];

            return _byUser.Values
                .Where(other => other.UserId != userId && IsFresh(other, now) && AreClose(me, other))
                .OrderBy(other => other.StartedAt)
                .Take(MaxListed)
                .Select(other => new Candidate(
                    other.UserId,
                    other.Token,
                    YouAccepted: me.Accepted.Contains(other.Token),
                    AcceptedYou: other.Accepted.Contains(me.Token)))
                .ToList();
        }
    }

    /// <summary>Resolves a token from the caller's list to the account behind it, if that person is still on
    /// and close. Used to apply the caller's own block and crew filters before accepting.</summary>
    public string? ResolveNearby(string userId, string token)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            return _byUser.TryGetValue(userId, out var me)
                && _byToken.TryGetValue(token, out var other)
                && other.UserId != userId
                && IsFresh(me, now) && IsFresh(other, now) && AreClose(me, other)
                ? other.UserId
                : null;
        }
    }

    /// <summary>The caller accepts the person behind <paramref name="token"/>. Only one of two people
    /// accepting at the same moment sees <see cref="AcceptOutcome.Mutual"/>, because the check and the
    /// record happen under one lock.</summary>
    public (AcceptOutcome Outcome, string? OtherUserId) Accept(string userId, string token)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            Purge(now);
            if (!_byUser.TryGetValue(userId, out var me))
                return (AcceptOutcome.NotOn, null);
            if (!_byToken.TryGetValue(token, out var other) || other.UserId == userId
                || !IsFresh(me, now) || !IsFresh(other, now) || !AreClose(me, other))
                return (AcceptOutcome.NotFound, null);

            if (other.Accepted.Contains(me.Token))
            {
                other.Accepted.Remove(me.Token);
                me.Accepted.Remove(other.Token);
                return (AcceptOutcome.Mutual, other.UserId);
            }

            me.Accepted.Add(other.Token);
            return (AcceptOutcome.Waiting, other.UserId);
        }
    }

    /// <summary>Notes a new crew member on the person's window, so their next refresh reports it (the other
    /// person may not be the one whose call made the link).</summary>
    public void RecordAdded(string userId, CrewMember member)
    {
        lock (_lock)
        {
            if (_byUser.TryGetValue(userId, out var finder))
                finder.Added.Insert(0, member);
        }
    }

    public IReadOnlyList<CrewMember> Added(string userId)
    {
        lock (_lock)
            return _byUser.TryGetValue(userId, out var finder) ? finder.Added.ToList() : [];
    }

    private static bool IsFresh(Finder finder, DateTimeOffset now) => now - finder.FixAt <= FixFreshFor;

    private static bool AreClose(Finder a, Finder b) =>
        NearbyJoin.IsWithinRadius(
            GeoMath.DistanceMeters(a.Fix.Latitude, a.Fix.Longitude, b.Fix.Latitude, b.Fix.Longitude),
            RadiusMeters, a.Fix.AccuracyMeters, b.Fix.AccuracyMeters);

    private void Purge(DateTimeOffset now)
    {
        foreach (var expired in _byUser.Values.Where(f => now - f.StartedAt >= Window).ToList())
            RemoveLocked(expired.UserId);
    }

    private void RemoveLocked(string userId)
    {
        if (!_byUser.Remove(userId, out var finder))
            return;
        _byToken.Remove(finder.Token);
        foreach (var other in _byUser.Values)
            other.Accepted.Remove(finder.Token);
    }

    private bool TryCountStart(string userId, DateTimeOffset now)
    {
        if (_starts.Count >= MaxTrackedUsers && !_starts.ContainsKey(userId))
            _starts.Clear(); // a flood of accounts can't grow this without bound

        var window = _starts.GetValueOrDefault(userId) ?? new StartWindow(now, 0);
        if (now - window.Start >= TimeSpan.FromHours(1))
            window = new StartWindow(now, 0);
        if (window.Count >= StartsPerUserPerHour)
        {
            _starts[userId] = window;
            return false;
        }

        _starts[userId] = window with { Count = window.Count + 1 };
        return true;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public record Fix(double Latitude, double Longitude, double? AccuracyMeters);

    private sealed record StartWindow(DateTimeOffset Start, int Count);

    private sealed class Finder(string userId, string token, DateTimeOffset startedAt, Fix fix, DateTimeOffset fixAt)
    {
        public string UserId { get; } = userId;
        public string Token { get; } = token;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public Fix Fix { get; set; } = fix;
        public DateTimeOffset FixAt { get; set; } = fixAt;
        public int Refreshes { get; set; }
        /// <summary>Tokens of the people this person has accepted.</summary>
        public HashSet<string> Accepted { get; } = new();
        public List<CrewMember> Added { get; } = new();
    }
}
