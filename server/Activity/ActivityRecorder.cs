using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace DotWatcher.Server;

/// <summary>One row of activity_events, before it is written.</summary>
public sealed record ActivityEvent(
    DateTimeOffset At,
    string UserId,
    string Kind,
    string? OtherUserId = null,
    string? DeviceId = null,
    string? Client = null,
    string? AppVersion = null,
    string? RunId = null,
    string? SessionId = null,
    string? DedupeKey = null,
    string? DetailJson = null);

/// <summary>
/// What a controller tells the request-logging middleware about the activity event its request makes:
/// whose line it goes on (when that isn't the signed-in caller), the run, a dedupe key, extra detail,
/// or that nothing happened and no event should be written. Kept in <see cref="HttpContext.Items"/>,
/// like the <c>Log:</c> keys.
/// </summary>
public static class ActivityContext
{
    private const string Key = "Activity:Info";

    public sealed class Info
    {
        public string? UserId { get; set; }
        // Which of the route's kinds this request makes, when the route has several (POST /me/activity).
        public string? Kind { get; set; }
        public string? RunId { get; set; }
        public string? DedupeKey { get; set; }
        public object? Detail { get; set; }
        public bool Skip { get; set; }
    }

    /// <summary>Adds to what the event records. Only the arguments given are changed.</summary>
    public static void SetActivity(
        this HttpContext context, string? userId = null, string? runId = null, string? dedupeKey = null, object? detail = null,
        string? kind = null)
    {
        var info = Get(context) ?? new Info();
        info.UserId = userId ?? info.UserId;
        info.Kind = kind ?? info.Kind;
        info.RunId = runId ?? info.RunId;
        info.DedupeKey = dedupeKey ?? info.DedupeKey;
        info.Detail = detail ?? info.Detail;
        context.Items[Key] = info;
    }

    /// <summary>The request succeeded but nothing happened (a repeat stop), so no event is written.</summary>
    public static void SkipActivity(this HttpContext context)
    {
        var info = Get(context) ?? new Info();
        info.Skip = true;
        context.Items[Key] = info;
    }

    public static Info? Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) ? value as Info : null;

    /// <summary>A short, one-way key for a viewer (a browser id or an IP), so the raw value is never stored.</summary>
    public static string ViewerKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}

/// <summary>
/// Takes activity events from the request pipeline and gets them into the database without slowing or
/// failing a request: they are queued and <see cref="ActivityWriterService"/> writes them in batches.
/// With <c>Activity:WriteImmediately</c> (the tests) each event is written as it is recorded.
/// </summary>
public sealed class ActivityRecorder(SessionStore store, IConfiguration configuration, ILogger<ActivityRecorder> logger)
{
    private const int MaxRemembered = 20_000;

    private readonly Channel<ActivityEvent> _queue = Channel.CreateBounded<ActivityEvent>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    // Dedupe keys already recorded by this process, so a watch page polling every 10 s doesn't send
    // an insert each time. The database's unique index is what actually guarantees one row.
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    private readonly bool _writeImmediately = configuration.GetValue<bool>("Activity:WriteImmediately");

    public ChannelReader<ActivityEvent> Queue => _queue.Reader;

    public void Record(ActivityEvent activity)
    {
        if (activity.DedupeKey is { } key)
        {
            if (_seen.Count > MaxRemembered)
                _seen.Clear();
            if (!_seen.TryAdd(key, 0))
                return;
        }

        if (_writeImmediately)
        {
            Write([activity]);
            return;
        }

        if (!_queue.Writer.TryWrite(activity))
            logger.LogWarning("Activity queue is full; dropped {Kind} for {UserId}", activity.Kind, activity.UserId);
    }

    public void Write(IReadOnlyList<ActivityEvent> batch)
    {
        try
        {
            store.InsertActivityEvents(batch);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write {Count} activity events", batch.Count);
        }
    }
}

/// <summary>Drains <see cref="ActivityRecorder"/>'s queue into the database, a batch at a time.</summary>
public sealed class ActivityWriterService(ActivityRecorder recorder) : BackgroundService
{
    private const int MaxBatch = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = recorder.Queue;
        var batch = new List<ActivityEvent>(MaxBatch);
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < MaxBatch && reader.TryRead(out var activity))
                    batch.Add(activity);
                recorder.Write(batch);
                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
