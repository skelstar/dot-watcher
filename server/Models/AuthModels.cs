namespace DotWatcher.Server;

public record AuthenticatedUser(
    string UserId,
    string Username,
    string DisplayName
);

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
    string DisplayName
);

public record AdminUserSummary(
    string Id,
    string Username,
    string DisplayName,
    string CreatedAt
);

public record AdminSessionSummary(
    string SessionId,
    string SessionName,
    string InviteCode,
    string OwnerUsername,
    int MemberCount,
    string CreatedAt
);

public record AdminLocationRecord(
    string RunnerName,
    double Latitude,
    double Longitude,
    double? Heading,
    string Timestamp
);

public record AdminMemberStats(
    string DisplayName,
    string Role,
    int PositionCount,
    string? LastPositionAt
);

