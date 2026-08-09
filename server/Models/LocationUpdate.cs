namespace DotWatcher.Server;

public record LocationUpdate(
    string? RunnerName,
    string? SessionId,
    double? Latitude,
    double? Longitude,
    double? Heading,
    DateTimeOffset? Timestamp,
    // Self-reported: when the client expects to post next, at its current cadence (normal or a
    // slower one, e.g. satellite). Optional — older clients won't send it. See
    // .ai/plans/POST-nextExpectedAt.md.
    DateTimeOffset? NextExpectedAt = null
);
