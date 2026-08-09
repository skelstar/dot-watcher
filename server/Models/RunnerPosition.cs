namespace DotWatcher.Server;

public record RunnerPosition(
    string RunnerName,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp,
    // Self-reported heartbeat from the client's POST — when it expects to post next. Null for
    // older clients or the demo runner. Additive field: see .ai/plans/POST-nextExpectedAt.md.
    DateTimeOffset? NextExpectedAt = null
);
