namespace DotWatcher.Server;

public record RunnerPosition(
    string RunnerName,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp,
    // Self-reported heartbeat from the client's POST — when it expects to post next. Null for
    // older clients, the demo runner, or any pre-2026-08-12 row (persisted since then; see
    // AddPosition). Additive field: see .ai/plans/POST-nextExpectedAt.md.
    DateTimeOffset? NextExpectedAt = null,
    // Self-reported NWPath.isUltraConstrained from the client's POST — see LocationUpdate.cs for
    // why this isn't called IsSatellite. False for older clients, the demo runner, or any
    // pre-2026-08-12 row (persisted since then; see AddPosition).
    bool IsUltraConstrained = false,
    // Self-reported UIDevice.batteryLevel (0-100) from the client's POST. Null for older clients,
    // Android, the demo runner, or any pre-2026-09 row, and whenever the reporting device's
    // battery monitoring is unavailable — never inferred as a dead battery. See
    // .ai/plans/battery-level-reporting.md.
    int? BatteryLevel = null,
    // Set only on casual positions (GET /casual/live and the POST /location reply for casual), so
    // one shape serves named and casual sessions; see .ai/plans/unify-position-model.md.
    // InSession is the lock: out in a named session rather than casually. IsSelf marks the caller's
    // own dot. Initials is always the account's initials, for the letters on the dot, because
    // RunnerName can be a crew nickname ("Sean" would be cut down to "SE").
    bool InSession = false,
    bool IsSelf = false,
    string? Initials = null
);
