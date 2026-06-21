namespace DotWatcher.Server;

public record LocationUpdate(
    string? RunnerName,
    string? SessionCode,
    double? Latitude,
    double? Longitude,
    double? Heading,
    DateTimeOffset? Timestamp
);
