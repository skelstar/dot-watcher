using System.Collections.Concurrent;

namespace DotWatcher.Server;

public sealed class AuthAttemptLimiter
{
    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new();
    private readonly int _maxFailedAttempts;
    private readonly TimeSpan _lockoutDuration;
    private readonly TimeSpan _attemptWindowDuration;
    private readonly int _maxTrackedAttempts;
    private readonly TimeProvider _timeProvider;

    public AuthAttemptLimiter(IConfiguration configuration)
        : this(configuration, TimeProvider.System)
    {
    }

    public AuthAttemptLimiter(IConfiguration configuration, TimeProvider timeProvider)
    {
        _maxFailedAttempts = Math.Max(1, configuration.GetValue<int>("AuthMaxFailedAttempts", 5));
        _lockoutDuration = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue<int>("AuthLockoutMinutes", 15)));
        _attemptWindowDuration = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue<int>(
            "AuthAttemptWindowMinutes",
            (int)Math.Ceiling(_lockoutDuration.TotalMinutes))));
        _maxTrackedAttempts = Math.Max(1, configuration.GetValue<int>("AuthMaxTrackedAttempts", 10_000));
        _timeProvider = timeProvider;
    }

    public int TrackedAttemptCount => _attempts.Count;

    public bool IsLocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_attempts.TryGetValue(key, out var window))
            return false;

        var now = _timeProvider.GetUtcNow();
        lock (window)
        {
            if (IsExpired(window, now))
            {
                _attempts.TryRemove(key, out _);
                return false;
            }

            if (window.LockedUntil <= now)
            {
                if (window.LockedUntil != default)
                {
                    window.FailedAttempts = 0;
                    window.LockedUntil = default;
                }

                return false;
            }

            retryAfter = window.LockedUntil - now;
            return true;
        }
    }

    public void RecordFailure(string key)
    {
        var now = _timeProvider.GetUtcNow();
        if (_attempts.Count >= _maxTrackedAttempts)
            Prune(now);

        var window = _attempts.GetOrAdd(key, _ => new AttemptWindow());
        lock (window)
        {
            if (IsExpired(window, now))
            {
                window.FailedAttempts = 0;
                window.LockedUntil = default;
            }

            window.LastAttemptAt = now;
            window.FailedAttempts++;
            if (window.FailedAttempts >= _maxFailedAttempts)
                window.LockedUntil = now.Add(_lockoutDuration);
        }

        if (_attempts.Count > _maxTrackedAttempts)
            Prune(now);
    }

    public void RecordSuccess(string key) =>
        _attempts.TryRemove(key, out _);

    public static string KeyFor(HttpContext context, string username)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"{remoteIp}:{username}";
    }

    private sealed class AttemptWindow
    {
        public int FailedAttempts { get; set; }
        public DateTimeOffset LockedUntil { get; set; }
        public DateTimeOffset LastAttemptAt { get; set; }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, window) in _attempts)
        {
            lock (window)
            {
                if (IsExpired(window, now))
                    _attempts.TryRemove(key, out _);
            }
        }

        var overflow = _attempts.Count - _maxTrackedAttempts;
        if (overflow <= 0)
            return;

        foreach (var key in _attempts
            .Where(pair => pair.Value.LockedUntil <= now)
            .OrderBy(pair => pair.Value.LastAttemptAt)
            .Take(overflow)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _attempts.TryRemove(key, out _);
        }

        overflow = _attempts.Count - _maxTrackedAttempts;
        if (overflow <= 0)
            return;

        foreach (var key in _attempts
            .OrderBy(pair => pair.Value.LastAttemptAt)
            .Take(overflow)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _attempts.TryRemove(key, out _);
        }
    }

    private bool IsExpired(AttemptWindow window, DateTimeOffset now)
    {
        if (window.LockedUntil > now)
            return false;

        if (window.LockedUntil != default)
            return true;

        return window.LastAttemptAt != default &&
            window.LastAttemptAt.Add(_attemptWindowDuration) <= now;
    }
}
