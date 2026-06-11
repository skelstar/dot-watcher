namespace DotChaser.Server;

public record LocationUpdate(
    string RunnerName,
    string SessionCode,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp
);

public record RunnerPosition(
    string RunnerName,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp
);
