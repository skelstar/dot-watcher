using System.Collections.Concurrent;
using System.Text.Json;

namespace DotWatcher.Server;

/// <summary>
/// How long each person watching a Go run keeps the watch page on screen. The page polls every 10 s
/// and skips its polls while its tab is hidden, so a run of polls from one browser with no gap longer
/// than <see cref="Gap"/> is one viewing. Each viewing is one <c>watch_link_opened</c> event; when it
/// goes quiet its length is written onto that event as <c>watchedSeconds</c>. Kept in memory: a
/// restart mid-viewing loses only that viewing's length. See .ai/plans/activity-flow.md.
/// </summary>
public sealed class WatchViewingTracker(SessionStore store, IConfiguration configuration, ILogger<WatchViewingTracker> logger)
{
    private const string Kind = "watch_link_opened";
    private const int MaxViewings = 20_000;

    private sealed class Viewing(string dedupeKey, DateTimeOffset start)
    {
        public string DedupeKey { get; } = dedupeKey;
        public DateTimeOffset Start { get; } = start;
        public DateTimeOffset Last { get; set; } = start;
    }

    // run id + viewer -> their current viewing.
    private readonly ConcurrentDictionary<string, Viewing> _viewings = new();

    /// <summary>A viewing ends after this long without a poll: the tab was hidden or closed.</summary>
    public TimeSpan Gap { get; } = TimeSpan.FromSeconds(configuration.GetValue("Activity:ViewingGapSeconds", 30.0));

    /// <summary>
    /// One poll of a run's watch link by one viewer. Returns the dedupe key for a new viewing's
    /// <c>watch_link_opened</c> event, or null when the poll continues the viewing already recorded.
    /// </summary>
    public string? Poll(string runId, string viewer, DateTimeOffset now)
    {
        var key = $"{runId}:{viewer}";
        if (_viewings.TryGetValue(key, out var current) && now - current.Last <= Gap)
        {
            current.Last = now;
            return null;
        }

        // A viewing that went quiet but hasn't been swept yet ends here, before the new one starts.
        if (current is not null && _viewings.TryRemove(key, out var ended))
            Finish(ended);

        if (_viewings.Count > MaxViewings)
            Sweep(now);

        var viewing = new Viewing($"{key}:{now.ToUnixTimeMilliseconds()}", now);
        _viewings[key] = viewing;
        return viewing.DedupeKey;
    }

    /// <summary>Ends every viewing with no poll for longer than <see cref="Gap"/> and writes its length.</summary>
    public int Sweep(DateTimeOffset now)
    {
        var ended = 0;
        foreach (var (key, viewing) in _viewings)
        {
            if (now - viewing.Last <= Gap || !_viewings.TryRemove(key, out var removed))
                continue;
            Finish(removed);
            ended++;
        }
        return ended;
    }

    private void Finish(Viewing viewing)
    {
        try
        {
            var detail = JsonSerializer.Serialize(new
            {
                watchedSeconds = (int)Math.Round((viewing.Last - viewing.Start).TotalSeconds),
                lastSeenAt = viewing.Last.UtcDateTime,
            });
            // ActivityCapture prefixes the kind to the controller's dedupe key.
            store.MergeActivityDetail($"{Kind}:{viewing.DedupeKey}", detail);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write a watch viewing's length");
        }
    }
}

/// <summary>Ends quiet viewings every few seconds, so their lengths reach the Activity tab.</summary>
public sealed class WatchViewingSweepService(WatchViewingTracker tracker, TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                tracker.Sweep(timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
