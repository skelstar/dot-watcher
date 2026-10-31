namespace DotWatcher.Server;

// Casual tracking (see .ai/plans/casual-tracking.md). Every shape here is new and only reached by
// accounts with the "casual" feature, so none of it changes what an installed build already sees.

// HideStartArea: people who aren't in the runner's crew don't see them until they are
// CasualVisibility.StartFuzzMeters from where the run began. On unless the runner turns it off; crew
// are never affected.
public record CasualSettings(
    bool VisibleToNonCrew,
    bool HideStartArea = true
);

public record UpdateCasualSettingsRequest(
    bool? VisibleToNonCrew = null,
    bool? HideStartArea = null
);

public record CasualStartResponse(
    string SessionId,
    string RunId
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
    IReadOnlyList<RunnerPosition[]> Positions,
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
    DateTimeOffset ReceivedAt,
    // The runner's own setting: when true, non-crew don't see them near where the run began.
    bool HideStartArea = true
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
public record CasualAccount(bool VisibleToNonCrew, IReadOnlyList<string> GrantedFeatures, bool HideStartArea = true);

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
