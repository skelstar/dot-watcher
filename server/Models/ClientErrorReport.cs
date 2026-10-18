namespace DotWatcher.Server;

/// <summary>
/// A crash report a client uploads on its next launch after an uncaught exception killed it.
/// Everything is optional apart from <see cref="StackTrace"/>, and every string is length-capped on
/// receipt (see <c>ClientErrorsController</c>) since this endpoint is unauthenticated.
/// </summary>
public record ClientErrorReport(
    string? StackTrace,
    string? ThreadName,
    DateTimeOffset? OccurredAt,
    // How many times the app crashed since the last report was sent (a crash loop writes the same
    // pending file repeatedly, and only the latest trace is kept).
    int? Occurrences,
    string? AppVersion,
    string? OsVersion,
    string? DeviceModel
);
