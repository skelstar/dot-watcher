namespace DotWatcher.Server;

// Casual tracking (see .ai/plans/casual-tracking.md). Every shape here is new and only reached by
// accounts with the "casual" feature, so none of it changes what an installed build already sees.

public record CasualSettings(
    bool VisibleToNonCrew
);

public record UpdateCasualSettingsRequest(
    bool? VisibleToNonCrew = null
);

public record CasualStartResponse(
    string SessionId,
    string RunId
);

/// <summary>
/// The caller or a crew member. Same fields as <see cref="RunnerPosition"/> (so a client can decode
/// it as one) plus <see cref="InSession"/> (the lock: out in a named session rather than casually)
/// and <see cref="IsSelf"/>. <see cref="RunnerName"/> is the crew nickname, else the account's initials.
/// </summary>
public record CasualPosition(
    string RunnerName,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp,
    DateTimeOffset? NextExpectedAt,
    bool IsUltraConstrained,
    int? BatteryLevel,
    bool InSession,
    bool IsSelf
);

/// <summary>
/// Someone who isn't crew. Deliberately has no name, no id and no timestamp: nothing here lets a
/// client tell two polls' dots are the same person or build a track from them.
/// </summary>
public record NearbyDot(
    double Latitude,
    double Longitude,
    double? Heading,
    bool InSession
);

/// <summary>Participants and positions use the same shape as POST /location (positions are one-element arrays).</summary>
public record CasualLiveResponse(
    IReadOnlyList<string> Participants,
    IReadOnlyList<CasualPosition[]> Positions,
    IReadOnlyList<NearbyDot> Nearby
);

/// <summary>One person currently out, as held in the store's live snapshot.</summary>
public record CasualLiveEntry(
    string UserId,
    RunnerPosition Position,
    bool InSession,
    bool VisibleToNonCrew,
    double? StartLatitude,
    double? StartLongitude
);
