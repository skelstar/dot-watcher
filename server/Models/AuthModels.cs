namespace DotWatcher.Server;

public record AuthenticatedUser(
    string UserId,
    string Username,
    string DisplayName,
    // What this account can use (e.g. "crew"). Filled in for login, register and GET /me; null when
    // the user was read from a bearer token, which doesn't carry it.
    IReadOnlyList<string>? Features = null,
    // Server-driven settings (e.g. the palette). Same fill rule as Features; omitted from the JSON when null.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    UserConfig? Config = null
);

/// <summary>Admin/ops: replaces the features granted to an account.</summary>
public record SetFeaturesRequest(IReadOnlyList<string>? Features);

public record SetFeatureSwitchRequest(bool? Enabled);

public record FeatureSwitch(string Feature, bool Enabled);

public record UserAccount(
    string Id,
    string Username,
    string DisplayName,
    string PasswordHash
);

public record RegisterRequest(
    string Username,
    string Password,
    string DisplayName
);

public record LoginRequest(
    string Username,
    string Password
);

public record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUser User
);

public record IssuedUserToken(
    string Value,
    DateTimeOffset ExpiresAt,
    string TokenId
);

public record ValidatedUserToken(
    string TokenId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset AcceptedUntil
);

public record CreateSessionRequest(
    string? SessionName = null,
    string? DisplayName = null,
    int? MaxLengthHours = null,
    bool? Nearby = null,
    int? NearbyRadiusMeters = null,
    double? Latitude = null,
    double? Longitude = null,
    double? Accuracy = null,
    // Groups (see .ai/plans/go-and-groups.md): 4-8 letters or digits; nearby sessions only, and only
    // for accounts with the "groups" feature.
    string? EntryCode = null
);

/// <summary>The organiser's Ping: a fresh fix sets the meeting point; Enabled/RadiusMeters change the nearby setting.</summary>
public record UpdateNearbyRequest(
    double? Latitude = null,
    double? Longitude = null,
    double? Accuracy = null,
    bool? Enabled = null,
    int? RadiusMeters = null
);

/// <summary>Joining a nearby session: the joiner's current fix, which the server checks against the radius.</summary>
public record JoinNearbyRequest(
    double? Latitude = null,
    double? Longitude = null,
    double? Accuracy = null,
    string? DisplayName = null,
    // The organiser's entry code, when the tile said one is needed. Case and spaces don't matter.
    string? EntryCode = null
);

/// <summary>Admin/ops: creates a session owned by an existing account, for callers (e.g. the admin
/// panel's NDJSON import) that have no session-owning user token of their own.</summary>
public record AdminCreateSessionRequest(
    string OwnerUsername,
    string? SessionName = null,
    string? DisplayName = null
);

public record JoinSessionRequest(
    string? DisplayName = null,
    string? Role = null
);

public record SessionMembership(
    string SessionId,
    string SessionName,
    string InviteCode,
    string Role,
    string DisplayName,
    string OwnerDisplayName,
    // ISO-8601 UTC creation time of the session (not the membership), so clients can tell
    // same-named sessions apart ("3 days ago"). Additive; older clients ignore it.
    string? CreatedAt = null,
    // ISO-8601 UTC time the session (a group, see .ai/plans/go-and-groups.md) ends: created_at plus its
    // event length (max_length_hours, default 24 h). Additive; older clients ignore it.
    string? EndsAt = null,
    // Whether the caller owns (organised) this session, so a phone can show the organiser's controls on
    // the start screen. Additive; older clients ignore it.
    bool IsOwner = false,
    // The owner's view of whether people nearby can find it; null for everyone else (not their business,
    // and an older client never reads it).
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? Nearby = null
);

public record BlockedUser(
    string UserId,
    string DisplayName,
    string BlockedAt
);

/// <summary>A person in the caller's crew. Deliberately has no username: usernames are login
/// identifiers (and may become email addresses), so crew members only ever see a nickname the
/// person chose, falling back to the account's initials (<see cref="DisplayName"/>).</summary>
public record CrewMember(
    string UserId,
    string? Nickname,
    string DisplayName,
    // ISO-8601 UTC time the two became crew.
    string Since
);

public record CrewOverview(
    IReadOnlyList<CrewMember> Crew
);

/// <summary>A single-use, expiring code that lets one person add the caller to their crew.</summary>
public record CrewCode(
    string Code,
    string ExpiresAt
);

/// <summary>Who a crew code belongs to, shown on the confirm screen before it is redeemed.</summary>
public record CrewCodePreview(
    string UserId,
    string? Nickname,
    string DisplayName,
    bool AlreadyCrew
);

public record UpdateProfileRequest(
    string? Nickname = null
);

public record ProfileResponse(
    string? Nickname
);

public enum CrewCodeOutcome
{
    Ok,
    // Unknown, expired, already used, malformed, or the two people have blocked each other. These
    // are deliberately indistinguishable to the caller.
    Invalid,
    OwnCode,
    CrewFull,
}

public enum CrewLinkOutcome
{
    Ok,
    // Blocked either way round, or an account that no longer exists.
    Blocked,
    CrewFull,
}

/// <summary>A position for "Find new crew". Accuracy in metres; 100 or better.</summary>
public record CrewFindingRequest(
    double? Latitude = null,
    double? Longitude = null,
    double? Accuracy = null
);

/// <summary>
/// Someone near the caller who also has "Find new crew" on. <see cref="Id"/> is a random token for
/// this person's current window, never their account id, and there is no position or distance.
/// </summary>
public record CrewNearbyPerson(
    string Id,
    string? Nickname,
    string DisplayName,
    // The caller tapped this person and accepted.
    bool YouAccepted,
    // This person tapped the caller and accepted; the caller accepting back makes them crew.
    bool AcceptedYou
);

/// <summary>The caller's "Find new crew" window.</summary>
public record CrewFindingState(
    // ISO-8601 UTC time the window closes by itself.
    string ExpiresAt,
    IReadOnlyList<CrewNearbyPerson> People,
    // People who became the caller's crew during this window, most recent first.
    IReadOnlyList<CrewMember> Added
);

public record SessionRunner(
    string UserId,
    string DisplayName
);

public record AdminUserSummary(
    string Id,
    string Username,
    string DisplayName,
    string CreatedAt,
    IReadOnlyList<string> Features,
    // Last authenticated request or sign-in (to within a few minutes); null if not seen since this was tracked.
    string? LastSeenAt = null
);

public record AdminSessionSummary(
    string SessionId,
    string SessionName,
    string InviteCode,
    string OwnerUsername,
    int MemberCount,
    string CreatedAt
);

public record AdminMemberStats(
    string DisplayName,
    string Role,
    int PositionCount,
    string? LastPositionAt
);

