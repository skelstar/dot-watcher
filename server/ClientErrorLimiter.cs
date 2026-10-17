using System.Collections.Concurrent;

namespace DotWatcher.Server;

/// <summary>
/// Caps how many crash reports <c>POST /client-errors</c> accepts, because that endpoint is
/// unauthenticated: a per-device cap (so one crash-looping phone can't flood the log) and an
/// overall cap (so a script can't either). Fixed one-hour windows, in memory, per server process.
/// A request over the cap is dropped, never queued; the client keeps its file and retries later.
/// </summary>
public sealed class ClientErrorLimiter(TimeProvider timeProvider)
{
    private const int PerDevicePerHour = 10;
    private const int OverallPerHour = 200;
    private const int MaxTrackedDevices = 5_000;

    private readonly ConcurrentDictionary<string, Window> _devices = new();
    private readonly object _lock = new();
    private Window _overall = new(default, 0);

    public bool TryAccept(string? deviceId)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (now - _overall.Start >= TimeSpan.FromHours(1))
                _overall = new Window(now, 0);
            if (_overall.Count >= OverallPerHour)
                return false;

            var key = string.IsNullOrWhiteSpace(deviceId) ? "(none)" : deviceId;
            if (_devices.Count >= MaxTrackedDevices && !_devices.ContainsKey(key))
                _devices.Clear(); // a flood of fake ids can't grow this without bound

            var window = _devices.GetOrAdd(key, _ => new Window(now, 0));
            if (now - window.Start >= TimeSpan.FromHours(1))
                window = new Window(now, 0);
            if (window.Count >= PerDevicePerHour)
            {
                _devices[key] = window;
                return false;
            }

            _devices[key] = window with { Count = window.Count + 1 };
            _overall = _overall with { Count = _overall.Count + 1 };
            return true;
        }
    }

    private sealed record Window(DateTimeOffset Start, int Count);
}
