namespace DotWatcher.Server;

public record AuthenticatedUser(
    string UserId,
    string Username,
    string DisplayName,
    // What this account can use (e.g. "crew"). Filled in for login, register and GET /me; null when
    // the user was read from a bearer token, which doesn't carry it.
    IReadOnlyList<string>? Features = null
);

/// <summary>Admin/ops: replaces the features granted to an account.</summary>
public record SetFeaturesRequest(IReadOnlyList<string>? Features);

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
    double? Accuracy = null
);

/// <summary>The organiser's Ping: a fresh fix sets the meeting point; Enabled/RadiusMeters change the nearby setting.</summary>
public record UpdateNearbyRequest(
    double? Latitude = null,
    double? Longitude = null,
    double? Accuracy = null,
    bool? Enabled = null,
    int? RadiusMeters = null
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
    string? CreatedAt = null
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

public record SessionRunner(
    string UserId,
    string DisplayName
);

public record AdminUserSummary(
    string Id,
    string Username,
    string DisplayName,
    string CreatedAt,
    IReadOnlyList<string> Features
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

