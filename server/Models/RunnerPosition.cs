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
    int? BatteryLevel = null
);
