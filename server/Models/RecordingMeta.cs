namespace DotWatcher.Server;

public record RecordingMeta(
    DateTimeOffset? RunStartTimestamp,
    DateTimeOffset? LatestTimestamp
);
