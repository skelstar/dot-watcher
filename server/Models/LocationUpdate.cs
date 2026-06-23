namespace DotWatcher.Server;

public record LocationUpdate(
    string? RunnerName,
    string? SessionId,
    double? Latitude,
    double? Longitude,
    double? Heading,
    DateTimeOffset? Timestamp
);
