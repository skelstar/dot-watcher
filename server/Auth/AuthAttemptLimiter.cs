using System.Collections.Concurrent;

namespace DotWatcher.Server;

public sealed class AuthAttemptLimiter
{
    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new();
    private readonly int _maxFailedAttempts;
    private readonly TimeSpan _lockoutDuration;

    public AuthAttemptLimiter(IConfiguration configuration)
    {
        _maxFailedAttempts = Math.Max(1, configuration.GetValue<int>("AuthMaxFailedAttempts", 5));
        _lockoutDuration = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue<int>("AuthLockoutMinutes", 15)));
    }

    public bool IsLocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_attempts.TryGetValue(key, out var window))
            return false;

        lock (window)
        {
            var now = DateTimeOffset.UtcNow;
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
        var window = _attempts.GetOrAdd(key, _ => new AttemptWindow());
        lock (window)
        {
            window.FailedAttempts++;
            if (window.FailedAttempts >= _maxFailedAttempts)
                window.LockedUntil = DateTimeOffset.UtcNow.Add(_lockoutDuration);
        }
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
    }
}
