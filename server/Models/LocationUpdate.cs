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
    DateTimeOffset? NextExpectedAt = null,
    // Self-reported: NWPath.isUltraConstrained at capture time (iOS 26.1+ only — older clients
    // and iOS versions always report false, not "unknown"). Named for what the OS actually
    // classifies rather than "IsSatellite" — Apple's own guidance is that this flags "treat this
    // path as ultra-constrained", not a specific medium; satellite just happens to be the only
    // real-world case that sets it today. Deliberately independent of NextExpectedAt/cadence:
    // a slower posting interval could exist for unrelated reasons (e.g. battery saving), and this
    // could in principle be true without the cadence having changed.
    bool IsUltraConstrained = false
);
