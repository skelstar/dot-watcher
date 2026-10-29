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
/// <see cref="DisplayName"/> is always the account's display name (its initials), for the letters on the dot, so a nickname
/// such as "Sean" is never cut down to "SE".
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
    bool IsSelf,
    string DisplayName
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
    double? StartLongitude,
    // When the server received the position. "Out" is judged from this, never from the phone's
    // own clock (Position.Timestamp), which can be wrong.
    DateTimeOffset ReceivedAt
);

public enum CasualPostOutcome
{
    // No open run (never started, stopped, or closed as abandoned). The client should start again.
    NoRun,
    // Stored and now shown as the runner's current position.
    Live,
    // Stored, but older than the stale window (a queued position flushed late), so it is kept for
    // replay and doesn't change where the runner is shown.
    Historical,
}

public record CasualPostResult(CasualPostOutcome Outcome, CasualLiveEntry? Self = null);

/// <summary>What the casual endpoints need to know about an account, read in one query.</summary>
public record CasualAccount(bool VisibleToNonCrew, IReadOnlyList<string> GrantedFeatures);

/// <summary>A new watch link for the caller's current run. <see cref="Path"/> is relative to the web
/// site (<c>/watch/{token}</c>); each client builds the full link with its own web base URL.</summary>
public record CasualWatchLinkResponse(string Token, string Path);

/// <summary>Where the watched runner was last heard from, on the server's clock.</summary>
public record WatchPosition(
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp,
    DateTimeOffset ReceivedAt
);

/// <summary>
/// What someone with a watch link sees: one runner, by initials, and nothing else. <see cref="Ended"/>
/// once the run has stopped (no position is returned then); <see cref="Live"/> while the server has
/// heard from the runner recently enough to call them out.
/// </summary>
public record CasualWatchView(
    string Label,
    bool Ended,
    bool Live,
    WatchPosition? Position
);
