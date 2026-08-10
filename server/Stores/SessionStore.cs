using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace DotWatcher.Server;

public class SessionStore(string connectionString)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // session id -> user id -> live position history
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RunnerPosition>>> _sessions = new();
    private readonly NpgsqlDataSource _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

    // Perpetual public demo session (see EnsureDemoSession): a permanent synthetic "runner" whose
    // position is computed live rather than posted/stored, so the session never archives (it
    // always has a runner) and never accumulates data (AddPosition skips persistence for it).
    private string? _demoUserId;
    private string _demoDisplayName = "DW";
    private double _demoAnchorLatitude;
    private double _demoAnchorLongitude;
    private double _demoLoopRadiusMeters;
    private double _demoLoopPeriodSeconds = 1;

    public string? DemoSessionId { get; private set; }

    public void Initialize()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS location_updates (
                id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                session_id     TEXT NOT NULL,
                runner_user_id TEXT,
                runner_name    TEXT NOT NULL,
                latitude       DOUBLE PRECISION NOT NULL,
                longitude      DOUBLE PRECISION NOT NULL,
                heading        DOUBLE PRECISION,
                timestamp      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_session        ON location_updates(session_id);
            CREATE INDEX IF NOT EXISTS idx_session_runner ON location_updates(session_id, runner_name);
            CREATE INDEX IF NOT EXISTS idx_session_timestamp ON location_updates(session_id, timestamp);

            CREATE TABLE IF NOT EXISTS users (
                id            TEXT PRIMARY KEY,
                username      TEXT NOT NULL UNIQUE,
                display_name  TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                created_at    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_sessions (
                id            TEXT PRIMARY KEY,
                session_name  TEXT NOT NULL,
                invite_code   TEXT NOT NULL UNIQUE,
                owner_user_id TEXT NOT NULL,
                created_at    TEXT NOT NULL,
                archived_at   TEXT,
                FOREIGN KEY(owner_user_id) REFERENCES users(id)
            );
            CREATE INDEX IF NOT EXISTS idx_app_sessions_invite ON app_sessions(invite_code);

            CREATE TABLE IF NOT EXISTS session_members (
                session_id   TEXT NOT NULL,
                user_id      TEXT NOT NULL,
                role         TEXT NOT NULL,
                display_name TEXT NOT NULL,
                joined_at    TEXT NOT NULL,
                left_at      TEXT,
                PRIMARY KEY(session_id, user_id),
                FOREIGN KEY(session_id) REFERENCES app_sessions(id),
                FOREIGN KEY(user_id)    REFERENCES users(id)
            );

            CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
            CREATE INDEX IF NOT EXISTS idx_session_members_user ON session_members(user_id);

            CREATE TABLE IF NOT EXISTS revoked_user_tokens (
                token_id   TEXT PRIMARY KEY,
                expires_at TEXT NOT NULL,
                revoked_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_revoked_user_tokens_expires_at ON revoked_user_tokens(expires_at);

            CREATE TABLE IF NOT EXISTS session_routes (
                session_id  TEXT PRIMARY KEY,
                gpx_content TEXT NOT NULL,
                uploaded_at TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES app_sessions(id)
            );

            CREATE TABLE IF NOT EXISTS blocked_users (
                blocker_user_id TEXT NOT NULL,
                blocked_user_id TEXT NOT NULL,
                created_at      TEXT NOT NULL,
                PRIMARY KEY(blocker_user_id, blocked_user_id),
                FOREIGN KEY(blocker_user_id) REFERENCES users(id),
                FOREIGN KEY(blocked_user_id) REFERENCES users(id)
            );
            CREATE INDEX IF NOT EXISTS idx_blocked_users_blocked ON blocked_users(blocked_user_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // Idempotent - safe to call on every startup. Resolves (creating if missing) the permanent
    // demo session and its permanent "runner" membership for DW, then caches DemoSessionId plus
    // the anchor/loop settings used by GetDemoRunnerPosition. DW's own password is never used;
    // it's a placeholder identity, not a real account anyone signs in as.
    public void EnsureDemoSession(
        string inviteCode,
        string displayName,
        double anchorLatitude,
        double anchorLongitude,
        double loopRadiusMeters,
        double loopPeriodSeconds)
    {
        _demoDisplayName = displayName;
        _demoAnchorLatitude = anchorLatitude;
        _demoAnchorLongitude = anchorLongitude;
        _demoLoopRadiusMeters = loopRadiusMeters;
        _demoLoopPeriodSeconds = loopPeriodSeconds;

        const string demoUsername = "dw-demo";
        CreateUser(new UserAccount(
            Guid.NewGuid().ToString(),
            demoUsername,
            displayName,
            PasswordHasher.Hash(Guid.NewGuid().ToString())));
        var demoUserId = GetUserByUsername(demoUsername)!.Id;
        _demoUserId = demoUserId;

        var normalizedInvite = NormalizeSessionName(inviteCode)
            ?? throw new InvalidOperationException($"Configured demo invite code '{inviteCode}' is not a valid invite code.");
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO app_sessions (id, session_name, invite_code, owner_user_id, created_at)
                VALUES (@id, @sessionName, @inviteCode, @ownerUserId, @createdAt)
                ON CONFLICT (invite_code) DO NOTHING
                """;
            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@sessionName", $"{displayName} Demo");
            insert.Parameters.AddWithValue("@inviteCode", normalizedInvite);
            insert.Parameters.AddWithValue("@ownerUserId", demoUserId);
            insert.Parameters.AddWithValue("@createdAt", now);
            insert.ExecuteNonQuery();
        }

        string demoSessionId;
        using (var lookup = conn.CreateCommand())
        {
            lookup.CommandText = "SELECT id FROM app_sessions WHERE invite_code = @inviteCode";
            lookup.Parameters.AddWithValue("@inviteCode", normalizedInvite);
            demoSessionId = (string)lookup.ExecuteScalar()!;
        }

        UpsertMembership(conn, demoSessionId, demoUserId, "runner", displayName, now);
        DemoSessionId = demoSessionId;
    }

    private bool IsDemoSession(string sessionId) => DemoSessionId is not null && sessionId == DemoSessionId;

    // Pure function of wall-clock time - DW's position is never posted or stored, just computed
    // fresh on every read as a small loop around the configured anchor point.
    private RunnerPosition GetDemoRunnerPosition(DateTimeOffset now)
    {
        var secondsIntoLoop = now.ToUnixTimeMilliseconds() / 1000.0 % _demoLoopPeriodSeconds;
        var angle = 2 * Math.PI * secondsIntoLoop / _demoLoopPeriodSeconds;

        const double metersPerDegreeLatitude = 111_320.0;
        var metersPerDegreeLongitude = metersPerDegreeLatitude * Math.Cos(_demoAnchorLatitude * Math.PI / 180.0);

        var latitude = _demoAnchorLatitude + _demoLoopRadiusMeters * Math.Sin(angle) / metersPerDegreeLatitude;
        var longitude = _demoAnchorLongitude + _demoLoopRadiusMeters * Math.Cos(angle) / metersPerDegreeLongitude;

        // Tangent direction of travel around the loop (d/dangle of the position above), as a
        // compass bearing in degrees clockwise from north.
        var headingRadians = Math.Atan2(-Math.Sin(angle), Math.Cos(angle));
        var heading = headingRadians * 180.0 / Math.PI;
        if (heading < 0) heading += 360.0;

        return new RunnerPosition(_demoDisplayName, latitude, longitude, heading, now);
    }

    private NpgsqlConnection Connect() => _dataSource.OpenConnection();

    public bool CreateUser(UserAccount account)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id, username, display_name, password_hash, created_at)
            VALUES (@id, @username, @displayName, @passwordHash, @createdAt)
            ON CONFLICT DO NOTHING
            """;
        cmd.Parameters.AddWithValue("@id", account.Id);
        cmd.Parameters.AddWithValue("@username", account.Username);
        cmd.Parameters.AddWithValue("@displayName", account.DisplayName);
        cmd.Parameters.AddWithValue("@passwordHash", account.PasswordHash);
        cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() == 1;
    }

    public UserAccount? GetUserByUsername(string username)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, display_name, password_hash
            FROM users
            WHERE username = @username
            """;
        cmd.Parameters.AddWithValue("@username", username);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new UserAccount(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public IReadOnlyList<AdminUserSummary> GetAllUsers()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, display_name, created_at
            FROM users
            ORDER BY created_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        var users = new List<AdminUserSummary>();
        while (reader.Read())
            users.Add(new AdminUserSummary(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return users;
    }

    public bool UserExists(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1
            FROM users
            WHERE id = @userId
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public void RevokeUserToken(string tokenId, DateTimeOffset expiresAt)
    {
        using var conn = Connect();
        DeleteExpiredRevokedTokens(conn, DateTimeOffset.UtcNow);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO revoked_user_tokens (token_id, expires_at, revoked_at)
            VALUES (@tokenId, @expiresAt, @revokedAt)
            ON CONFLICT DO NOTHING
            """;
        cmd.Parameters.AddWithValue("@tokenId", tokenId);
        cmd.Parameters.AddWithValue("@expiresAt", expiresAt.ToString("O"));
        cmd.Parameters.AddWithValue("@revokedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public bool IsUserTokenRevoked(string tokenId, DateTimeOffset now)
    {
        using var conn = Connect();
        DeleteExpiredRevokedTokens(conn, now);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1)
            FROM revoked_user_tokens
            WHERE token_id = @tokenId AND expires_at > @now
            """;
        cmd.Parameters.AddWithValue("@tokenId", tokenId);
        cmd.Parameters.AddWithValue("@now", now.ToString("O"));
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public bool DeleteUserAccount(string userId)
    {
        using var conn = Connect();
        var memberships = GetSessionMembersForUser(conn, userId);
        var ownedSessionIds = GetOwnedSessionIds(conn, userId);

        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM location_updates WHERE runner_user_id = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.ExecuteNonQuery();
        }

        foreach (var sessionId in ownedSessionIds)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM location_updates WHERE session_id = @sessionId";
                cmd.Parameters.AddWithValue("@sessionId", sessionId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM session_members WHERE session_id = @sessionId";
                cmd.Parameters.AddWithValue("@sessionId", sessionId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM session_routes WHERE session_id = @sessionId";
                cmd.Parameters.AddWithValue("@sessionId", sessionId);
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM app_sessions WHERE owner_user_id = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM session_members WHERE user_id = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.ExecuteNonQuery();
        }

        int deletedUsers;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM users WHERE id = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);
            deletedUsers = cmd.ExecuteNonQuery();
        }

        tx.Commit();

        foreach (var sessionId in ownedSessionIds)
            _sessions.TryRemove(sessionId, out _);

        foreach (var membership in memberships.Where(membership => !ownedSessionIds.Contains(membership.SessionId)))
        {
            if (_sessions.TryGetValue(membership.SessionId, out var session))
                session.TryRemove(userId, out _);
        }

        return deletedUsers > 0;
    }

    public SessionMembership? CreateSessionForUser(string userId, string displayName, string? requestedName = null)
    {
        var sessionName = NormalizeSessionName(requestedName) ?? requestedName?.Trim() ?? "Session";
        var sessionId = Guid.NewGuid().ToString();
        var inviteCode = GenerateCode(6);
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();

        using var dupCheck = conn.CreateCommand();
        dupCheck.CommandText = "SELECT 1 FROM app_sessions WHERE session_name = @name";
        dupCheck.Parameters.AddWithValue("@name", sessionName);
        if (dupCheck.ExecuteScalar() is not null)
            return null;

        using var sessionCmd = conn.CreateCommand();
        sessionCmd.CommandText = """
            INSERT INTO app_sessions (id, session_name, invite_code, owner_user_id, created_at)
            VALUES (@sessionId, @sessionName, @inviteCode, @ownerUserId, @createdAt)
            """;
        sessionCmd.Parameters.AddWithValue("@sessionId", sessionId);
        sessionCmd.Parameters.AddWithValue("@sessionName", sessionName);
        sessionCmd.Parameters.AddWithValue("@inviteCode", inviteCode);
        sessionCmd.Parameters.AddWithValue("@ownerUserId", userId);
        sessionCmd.Parameters.AddWithValue("@createdAt", now);
        sessionCmd.ExecuteNonQuery();

        UpsertMembership(conn, sessionId, userId, "runner", displayName, now);

        return new SessionMembership(sessionId, sessionName, inviteCode, "runner", displayName, displayName);
    }

    public (SessionMembership? Membership, bool Archived, bool Blocked) JoinSessionByInvite(string inviteCode, string userId, string displayName, string role = "viewer")
    {
        var normalizedInvite = NormalizeSessionName(inviteCode);
        if (normalizedInvite is null)
            return (null, false, false);

        using var conn = Connect();
        using var lookup = conn.CreateCommand();
        lookup.CommandText = """
            SELECT id, session_name, invite_code, archived_at, owner_user_id
            FROM app_sessions
            WHERE invite_code = @inviteCode
            """;
        lookup.Parameters.AddWithValue("@inviteCode", normalizedInvite);
        using var reader = lookup.ExecuteReader();
        if (!reader.Read())
            return (null, false, false);

        var sessionId = reader.GetString(0);
        var storedInviteCode = reader.GetString(2);
        if (!reader.IsDBNull(3))
            return (null, true, false);
        var ownerUserId = reader.GetString(4);
        reader.Close();

        if (AreBlocked(conn, ownerUserId, userId))
            return (null, false, true);

        var joinedAt = DateTimeOffset.UtcNow.ToString("O");
        UpsertMembership(conn, sessionId, userId, role, displayName, joinedAt, preserveExistingRole: true);

        return (GetMembership(conn, sessionId, userId, storedInviteCode)!, false, false);
    }

    public string? GetSessionIdByInviteCode(string inviteCode)
    {
        var normalizedInvite = NormalizeSessionName(inviteCode);
        if (normalizedInvite is null)
            return null;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM app_sessions WHERE invite_code = @inviteCode";
        cmd.Parameters.AddWithValue("@inviteCode", normalizedInvite);
        return cmd.ExecuteScalar() as string;
    }

    public string? GetInviteCodeBySessionId(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT invite_code FROM app_sessions WHERE id = @sessionId";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        return cmd.ExecuteScalar() as string;
    }

    // Public, tokenless summary for the no-login invite-code viewing path (web client's
    // /code/{code}) — just enough to show who created the session before any positions exist.
    public SessionInfo? GetSessionInfoByInviteCode(string inviteCode)
    {
        var normalizedInvite = NormalizeSessionName(inviteCode);
        if (normalizedInvite is null)
            return null;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.session_name, owner.display_name
            FROM app_sessions s
            JOIN session_members owner ON owner.session_id = s.id AND owner.user_id = s.owner_user_id
            WHERE s.invite_code = @inviteCode
            """;
        cmd.Parameters.AddWithValue("@inviteCode", normalizedInvite);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new SessionInfo(reader.GetString(0), reader.GetString(1)) : null;
    }

    public IReadOnlyList<SessionMembership> GetSessionsForUser(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name, owner.display_name
            FROM session_members m
            JOIN app_sessions s ON s.id = m.session_id
            JOIN session_members owner ON owner.session_id = s.id AND owner.user_id = s.owner_user_id
            WHERE m.user_id = @userId AND m.left_at IS NULL
            ORDER BY m.joined_at DESC
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        return ReadMemberships(cmd);
    }

    private static readonly TimeSpan RecentSessionActiveWindow = TimeSpan.FromSeconds(45);

    public IReadOnlyList<SessionMembership> GetRecentLeftSessions(string userId, int limit = 3)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name, owner.display_name
            FROM session_members m
            JOIN app_sessions s ON s.id = m.session_id
            JOIN session_members owner ON owner.session_id = s.id AND owner.user_id = s.owner_user_id
            WHERE m.user_id = @userId AND m.left_at IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM location_updates l
                  WHERE l.session_id = s.id AND l.timestamp >= @since
              )
            ORDER BY m.left_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@since", (DateTime.UtcNow - RecentSessionActiveWindow).ToString("O"));
        cmd.Parameters.AddWithValue("@limit", limit);
        return ReadMemberships(cmd);
    }

    private static IReadOnlyList<SessionMembership> ReadMemberships(NpgsqlCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var sessions = new List<SessionMembership>();
        while (reader.Read())
            sessions.Add(new SessionMembership(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));

        return sessions;
    }

    public SessionMembership? GetMembership(string sessionId, string userId)
    {
        using var conn = Connect();
        return GetMembership(conn, sessionId, userId);
    }

    private static SessionMembership? GetMembership(
        NpgsqlConnection conn,
        string sessionId,
        string userId,
        string? inviteCode = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name, owner.display_name
            FROM session_members m
            JOIN app_sessions s ON s.id = m.session_id
            JOIN session_members owner ON owner.session_id = s.id AND owner.user_id = s.owner_user_id
            WHERE m.session_id = @sessionId AND m.user_id = @userId AND m.left_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SessionMembership(reader.GetString(0), reader.GetString(1), inviteCode ?? reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    public bool CanReadSession(string sessionId, string userId) =>
        GetMembership(sessionId, userId) is not null;

    public bool CanWriteLocation(string sessionId, string userId)
    {
        var membership = GetMembership(sessionId, userId);
        return membership?.Role == "runner";
    }

    // Blocking is account-level and symmetric for read/write purposes: it doesn't matter
    // who blocked whom, neither party should see the other once either has blocked.
    public bool AreBlocked(string userId1, string userId2)
    {
        using var conn = Connect();
        return AreBlocked(conn, userId1, userId2);
    }

    private static bool AreBlocked(NpgsqlConnection conn, string userId1, string userId2)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM blocked_users
            WHERE (blocker_user_id = @user1 AND blocked_user_id = @user2)
               OR (blocker_user_id = @user2 AND blocked_user_id = @user1)
            """;
        cmd.Parameters.AddWithValue("@user1", userId1);
        cmd.Parameters.AddWithValue("@user2", userId2);
        return cmd.ExecuteScalar() is not null;
    }

    // Blocking ends shared-session membership immediately (the "second option" design: a
    // block also removes the blocked user from any session both are currently in), not just
    // a read-time filter. Unblocking does not restore that membership — same as any other
    // voluntary leave, the blocked user would need a fresh invite to rejoin.
    public void BlockUser(string blockerUserId, string blockedUserId)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO blocked_users (blocker_user_id, blocked_user_id, created_at)
                VALUES (@blocker, @blocked, @createdAt)
                ON CONFLICT DO NOTHING
                """;
            cmd.Parameters.AddWithValue("@blocker", blockerUserId);
            cmd.Parameters.AddWithValue("@blocked", blockedUserId);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.ExecuteNonQuery();
        }

        List<string> sharedSessionIds;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT a.session_id
                FROM session_members a
                JOIN session_members b ON b.session_id = a.session_id
                WHERE a.user_id = @blocker AND a.left_at IS NULL
                  AND b.user_id = @blocked AND b.left_at IS NULL
                """;
            cmd.Parameters.AddWithValue("@blocker", blockerUserId);
            cmd.Parameters.AddWithValue("@blocked", blockedUserId);
            using var reader = cmd.ExecuteReader();
            sharedSessionIds = [];
            while (reader.Read())
                sharedSessionIds.Add(reader.GetString(0));
        }

        foreach (var sessionId in sharedSessionIds)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE session_members
                    SET left_at = @leftAt
                    WHERE session_id = @sessionId AND user_id = @blocked AND left_at IS NULL
                    """;
                cmd.Parameters.AddWithValue("@leftAt", now);
                cmd.Parameters.AddWithValue("@sessionId", sessionId);
                cmd.Parameters.AddWithValue("@blocked", blockedUserId);
                cmd.ExecuteNonQuery();
            }

            if (_sessions.TryGetValue(sessionId, out var session))
                session.TryRemove(blockedUserId, out _);
        }

        tx.Commit();
    }

    public bool UnblockUser(string blockerUserId, string blockedUserId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM blocked_users
            WHERE blocker_user_id = @blocker AND blocked_user_id = @blocked
            """;
        cmd.Parameters.AddWithValue("@blocker", blockerUserId);
        cmd.Parameters.AddWithValue("@blocked", blockedUserId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<BlockedUser> GetBlockedUsers(string blockerUserId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.id, u.display_name, b.created_at
            FROM blocked_users b
            JOIN users u ON u.id = b.blocked_user_id
            WHERE b.blocker_user_id = @blocker
            ORDER BY b.created_at DESC
            """;
        cmd.Parameters.AddWithValue("@blocker", blockerUserId);
        using var reader = cmd.ExecuteReader();
        var blocked = new List<BlockedUser>();
        while (reader.Read())
            blocked.Add(new BlockedUser(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return blocked;
    }

    // Route management is an ownership action, not a membership role — session_members.role is
    // only ever "runner"/"viewer" (see UpsertMembership), while app_sessions.owner_user_id is the
    // actual creator/owner concept.
    public bool CanManageRoute(string sessionId, string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM app_sessions WHERE id = @sessionId AND owner_user_id = @userId";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public IReadOnlyList<SessionRunner> GetSessionRunners(string sessionId, string? callerUserId = null)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, display_name
            FROM session_members
            WHERE session_id = @sessionId AND role = 'runner' AND left_at IS NULL
            ORDER BY joined_at ASC
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        var runners = new List<SessionRunner>();
        while (reader.Read())
            runners.Add(new SessionRunner(reader.GetString(0), reader.GetString(1)));

        // In the demo session, a caller should only ever see DW and themselves - never other
        // real strangers who happen to also be trying the demo (see AddPosition/GetLatestPositions).
        if (IsDemoSession(sessionId))
            return runners.Where(r => r.UserId == _demoUserId || r.UserId == callerUserId).ToList();

        return runners;
    }

    public void AddPosition(ValidatedLocationUpdate update, string userId)
    {
        var sessionId = update.SessionId;

        var session = _sessions.GetOrAdd(sessionId, _ => new());
        var history = session.GetOrAdd(userId, _ => []);
        lock (history)
            history.Add(new RunnerPosition(
                update.RunnerName,
                update.Latitude,
                update.Longitude,
                update.Heading,
                update.Timestamp,
                update.NextExpectedAt));

        // The demo session never persists positions - live viewing above is unaffected (it reads
        // the in-memory cache, not Postgres), but nothing here ever needs cleaning up.
        if (IsDemoSession(sessionId))
            return;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp)
            VALUES (@sessionId, @userId, @name, @lat, @lon, @heading, @ts)
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@name", update.RunnerName);
        cmd.Parameters.AddWithValue("@lat", update.Latitude);
        cmd.Parameters.AddWithValue("@lon", update.Longitude);
        cmd.Parameters.Add(new NpgsqlParameter("heading", NpgsqlDbType.Double)
        {
            Value = update.Heading.HasValue ? update.Heading.Value : DBNull.Value
        });
        cmd.Parameters.AddWithValue("@ts", update.Timestamp.ToUniversalTime().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetParticipants(string sessionId, string? callerUserId = null)
    {
        var isDemo = IsDemoSession(sessionId);
        if (!_sessions.TryGetValue(sessionId, out var session))
            return isDemo ? [_demoDisplayName] : [];

        var participants = new List<string>(session.Count);
        foreach (var (userId, history) in session)
        {
            // In the demo session, only the caller's own name is included alongside DW's -
            // never another real visitor's (see AddPosition/GetLatestPositions).
            if (isDemo && userId != callerUserId)
                continue;

            lock (history)
            {
                if (history.Count > 0)
                    participants.Add(history[^1].RunnerName);
            }
        }

        if (isDemo)
            participants.Add(_demoDisplayName);

        return participants;
    }

    public IReadOnlyList<RunnerPosition[]> GetLatestPositions(string sessionId, string? callerUserId = null)
    {
        var isDemo = IsDemoSession(sessionId);
        var byUserId = new Dictionary<string, RunnerPosition>();
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            foreach (var (userId, history) in session)
            {
                // Same caller-only scoping as GetParticipants - the demo session never surfaces
                // another real visitor's position, only the caller's own plus DW's (below).
                if (isDemo && userId != callerUserId)
                    continue;

                lock (history)
                {
                    if (history.Count > 0)
                        byUserId[userId] = history[^1];
                }
            }
        }

        if (isDemo)
        {
            var result = byUserId.Values.Select(p => new[] { p }).ToList();
            result.Add([GetDemoRunnerPosition(DateTimeOffset.UtcNow)]);
            return result;
        }

        // Falls back to Postgres for any runner this pod's in-memory cache has nothing for - e.g.
        // a runner whose POST /location calls have all landed on a different pod (Staging and
        // Production share one database as of the 2026-07-22 cutover, but each still runs its own
        // process with its own private in-memory `_sessions`, so a runner posting to one is
        // invisible to a viewer polling the other). The in-memory copy is preferred when present
        // since it carries `nextExpectedAt`, which has no column in location_updates and so never
        // round-trips through Postgres.
        foreach (var (userId, position) in LoadLatestPositionsByRunner(sessionId))
            byUserId.TryAdd(userId, position);

        return byUserId.Values.Select(p => new[] { p }).ToList();
    }

    private Dictionary<string, RunnerPosition> LoadLatestPositionsByRunner(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT ON (runner_user_id)
                   runner_user_id, runner_name, latitude, longitude, heading, timestamp
            FROM location_updates
            WHERE session_id = @sessionId AND runner_user_id IS NOT NULL
            ORDER BY runner_user_id, timestamp DESC
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        var positions = new Dictionary<string, RunnerPosition>();
        while (reader.Read())
        {
            positions[reader.GetString(0)] = new RunnerPosition(
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                DateTimeOffset.Parse(reader.GetString(5)));
        }
        return positions;
    }

    public IReadOnlyList<AdminSessionSummary> GetAllSessions()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, s.invite_code, u.username, COUNT(m.user_id) AS member_count, s.created_at
            FROM app_sessions s
            JOIN users u ON u.id = s.owner_user_id
            LEFT JOIN session_members m ON m.session_id = s.id AND m.left_at IS NULL
            GROUP BY s.id, u.username
            ORDER BY s.created_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        var sessions = new List<AdminSessionSummary>();
        while (reader.Read())
            sessions.Add(new AdminSessionSummary(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), (int)reader.GetInt64(4), reader.GetString(5)));
        return sessions;
    }

    public bool LeaveSession(string sessionId, string userId)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE session_members
            SET left_at = @leftAt
            WHERE session_id = @sessionId AND user_id = @userId AND left_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@leftAt", now);
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        if (cmd.ExecuteNonQuery() == 0)
            return false;

        if (_sessions.TryGetValue(sessionId, out var session))
            session.TryRemove(userId, out _);

        // Once the last runner is gone the run is over: archive the session so nobody can
        // join or rejoin it. Replay stays available (recording endpoints don't check this).
        using var archive = conn.CreateCommand();
        archive.CommandText = """
            UPDATE app_sessions
            SET archived_at = @archivedAt
            WHERE id = @sessionId AND archived_at IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM session_members
                  WHERE session_id = @sessionId AND role = 'runner' AND left_at IS NULL
              )
            """;
        archive.Parameters.AddWithValue("@archivedAt", now);
        archive.Parameters.AddWithValue("@sessionId", sessionId);
        archive.ExecuteNonQuery();

        return true;
    }

    public bool DeleteSession(string sessionId)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        foreach (var table in new[] { "location_updates", "session_members", "session_routes" })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE session_id = @sessionId";
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.ExecuteNonQuery();
        }

        int rows;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM app_sessions WHERE id = @sessionId";
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            rows = cmd.ExecuteNonQuery();
        }

        tx.Commit();
        _sessions.TryRemove(sessionId, out _);
        return rows > 0;
    }

    public IEnumerable<string> GetRecordedSessions()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id
            FROM location_updates
            GROUP BY session_id
            ORDER BY MAX(timestamp) DESC
            """;
        using var reader = cmd.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    public bool HasRecording(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM location_updates WHERE session_id = @code";
        cmd.Parameters.AddWithValue("@code", sessionId);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    // A session's recording accumulates for as long as its invite code is reused, so a gap this
    // large between consecutive pings is treated as the boundary of a separate, earlier run.
    private static readonly TimeSpan RunGapThreshold = TimeSpan.FromMinutes(60);

    // Defensive cap on rows returned from a single windowed recording fetch.
    private const int MaxRecordingRows = 20000;

    public string GetRecordingAsNdjson(string sessionId)
    {
        var latestRun = LatestRun(LoadUpdatesByTimestamp(sessionId));
        return string.Join("\n", latestRun.Select(u => JsonSerializer.Serialize(u, _jsonOptions)));
    }

    // Returns NDJSON for the given time window, clamped to the current run so callers can never
    // scrub back past the boundary that GetRecordingAsNdjson/GetRecordingMeta already enforce.
    // truncated is true when more rows existed in-range than MaxRecordingRows allowed returning.
    public (string Ndjson, bool Truncated) GetRecordingWindowAsNdjson(
        string sessionId, DateTimeOffset? since, DateTimeOffset? until)
    {
        var runStart = GetRunStartTimestamp(sessionId);
        var effectiveSince = runStart is null ? since
            : since is null ? runStart
            : since < runStart ? runStart
            : since;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, runner_name, latitude, longitude, heading, timestamp
            FROM location_updates
            WHERE session_id = @code
              AND (@since IS NULL OR timestamp >= @since)
              AND (@until IS NULL OR timestamp <= @until)
            ORDER BY timestamp
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@code", sessionId);
        cmd.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.Text)
        {
            Value = (object?)effectiveSince?.ToString("O") ?? DBNull.Value
        });
        cmd.Parameters.Add(new NpgsqlParameter("until", NpgsqlDbType.Text)
        {
            Value = (object?)until?.ToString("O") ?? DBNull.Value
        });
        cmd.Parameters.AddWithValue("@limit", MaxRecordingRows + 1);

        var updates = new List<LocationUpdate>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                updates.Add(new LocationUpdate(
                    RunnerName: reader.GetString(1),
                    SessionId: reader.GetString(0),
                    Latitude: reader.GetDouble(2),
                    Longitude: reader.GetDouble(3),
                    Heading: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    Timestamp: DateTimeOffset.Parse(reader.GetString(5))
                ));
            }
        }

        var truncated = updates.Count > MaxRecordingRows;
        if (truncated)
            updates.RemoveRange(MaxRecordingRows, updates.Count - MaxRecordingRows);

        var ndjson = string.Join("\n", updates.Select(u => JsonSerializer.Serialize(u, _jsonOptions)));
        return (ndjson, truncated);
    }

    // Metadata about the current run's scrubbable range, without fetching any position rows.
    public RecordingMeta? GetRecordingMeta(string sessionId)
    {
        if (!HasRecording(sessionId))
            return null;

        var runStart = GetRunStartTimestamp(sessionId);

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(timestamp) FROM location_updates WHERE session_id = @code";
        cmd.Parameters.AddWithValue("@code", sessionId);
        var latest = cmd.ExecuteScalar() as string;

        return new RecordingMeta(
            RunStartTimestamp: runStart,
            LatestTimestamp: latest is null ? null : DateTimeOffset.Parse(latest));
    }

    // Finds where the most recent contiguous run begins, by scanning timestamps backward from
    // the latest and stopping at the first gap larger than RunGapThreshold.
    private DateTimeOffset? GetRunStartTimestamp(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT timestamp
            FROM location_updates
            WHERE session_id = @code
            ORDER BY timestamp DESC
            """;
        cmd.Parameters.AddWithValue("@code", sessionId);
        using var reader = cmd.ExecuteReader();

        DateTimeOffset? previous = null;
        DateTimeOffset? runStart = null;
        while (reader.Read())
        {
            var current = DateTimeOffset.Parse(reader.GetString(0));
            if (previous is not null && previous.Value - current > RunGapThreshold)
                break;
            runStart = current;
            previous = current;
        }

        return runStart;
    }

    private List<LocationUpdate> LoadUpdatesByTimestamp(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, runner_name, latitude, longitude, heading, timestamp
            FROM location_updates
            WHERE session_id = @code
            ORDER BY timestamp
            """;
        cmd.Parameters.AddWithValue("@code", sessionId);
        using var reader = cmd.ExecuteReader();
        var updates = new List<LocationUpdate>();
        while (reader.Read())
        {
            updates.Add(new LocationUpdate(
                RunnerName: reader.GetString(1),
                SessionId: reader.GetString(0),
                Latitude: reader.GetDouble(2),
                Longitude: reader.GetDouble(3),
                Heading: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                Timestamp: DateTimeOffset.Parse(reader.GetString(5))
            ));
        }
        return updates;
    }

    // Finds the start of the most recent contiguous run by scanning backward from the latest
    // timestamp and stopping at the first gap larger than RunGapThreshold.
    private static List<LocationUpdate> LatestRun(List<LocationUpdate> updatesByTimestamp)
    {
        if (updatesByTimestamp.Count == 0)
            return updatesByTimestamp;

        var cutoff = 0;
        for (var i = updatesByTimestamp.Count - 1; i > 0; i--)
        {
            if (updatesByTimestamp[i].Timestamp - updatesByTimestamp[i - 1].Timestamp > RunGapThreshold)
            {
                cutoff = i;
                break;
            }
        }

        return updatesByTimestamp.GetRange(cutoff, updatesByTimestamp.Count - cutoff);
    }

    public void SaveRecording(string sessionId, string ndjsonContent)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new LocationUpdateValidationException(["Session ID is required."]);

        var lines = ndjsonContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var updates = lines
            .Select((line, index) => ParseRecordingLine(line, sessionId, index + 1))
            .ToList();

        if (updates.Count == 0)
            throw new LocationUpdateValidationException(["Recording must contain at least one location update."]);

        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM location_updates WHERE session_id = @code";
            del.Parameters.AddWithValue("@code", sessionId);
            del.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp)
            VALUES (@code, NULL, @name, @lat, @lon, @heading, @ts)
            """;
        var pCode    = cmd.Parameters.Add("code",    NpgsqlDbType.Text);
        var pName    = cmd.Parameters.Add("name",    NpgsqlDbType.Text);
        var pLat     = cmd.Parameters.Add("lat",     NpgsqlDbType.Double);
        var pLon     = cmd.Parameters.Add("lon",     NpgsqlDbType.Double);
        var pHeading = cmd.Parameters.Add("heading", NpgsqlDbType.Double);
        var pTs      = cmd.Parameters.Add("ts",      NpgsqlDbType.Text);

        foreach (var u in updates)
        {
            pCode.Value    = sessionId;
            pName.Value    = u.RunnerName;
            pLat.Value     = u.Latitude;
            pLon.Value     = u.Longitude;
            pHeading.Value = u.Heading.HasValue ? (object)u.Heading.Value : DBNull.Value;
            pTs.Value      = u.Timestamp.ToUniversalTime().ToString("O");
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static ValidatedLocationUpdate ParseRecordingLine(string line, string sessionId, int lineNumber)
    {
        var parsed = JsonSerializer.Deserialize<LocationUpdate>(line, _jsonOptions)
            ?? throw new LocationUpdateValidationException([$"Line {lineNumber}: location update is required."]);

        var update = parsed with { SessionId = sessionId };
        if (!LocationUpdateValidation.TryValidate(update, out var validated, out var errors))
            throw new LocationUpdateValidationException(
                errors.Select(error => $"Line {lineNumber}: {error}").ToList());

        return validated;
    }

    public IReadOnlyList<AdminMemberStats> GetSessionMemberStats(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.display_name, m.role,
                   COUNT(l.id) AS position_count,
                   MAX(l.timestamp) AS last_position_at
            FROM session_members m
            LEFT JOIN location_updates l ON l.session_id = @sessionId AND l.runner_user_id = m.user_id
            WHERE m.session_id = @sessionId
            GROUP BY m.user_id, m.display_name, m.role
            ORDER BY m.joined_at ASC
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        var stats = new List<AdminMemberStats>();
        while (reader.Read())
            stats.Add(new AdminMemberStats(
                reader.GetString(0),
                reader.GetString(1),
                (int)reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return stats;
    }

    public IReadOnlyList<AdminLocationRecord> GetRecentLocationUpdates(string sessionId, int limit = 20)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT runner_name, latitude, longitude, heading, timestamp
            FROM location_updates
            WHERE session_id = @sessionId
            ORDER BY id DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        var records = new List<AdminLocationRecord>();
        while (reader.Read())
            records.Add(new AdminLocationRecord(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.GetString(4)));
        return records;
    }

    public bool DeleteRecording(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM location_updates WHERE session_id = @code";
        cmd.Parameters.AddWithValue("@code", sessionId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void ClearSession(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    public bool HasRoute(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM session_routes WHERE session_id = @code";
        cmd.Parameters.AddWithValue("@code", sessionId);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public string? GetRoute(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT gpx_content FROM session_routes WHERE session_id = @code";
        cmd.Parameters.AddWithValue("@code", sessionId);
        return cmd.ExecuteScalar() as string;
    }

    public void SaveRoute(string sessionId, string gpxContent)
    {
        if (string.IsNullOrWhiteSpace(gpxContent))
            throw new ArgumentException("GPX content is required.", nameof(gpxContent));

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_routes (session_id, gpx_content, uploaded_at)
            VALUES (@code, @gpx, @uploadedAt)
            ON CONFLICT(session_id) DO UPDATE SET
                gpx_content = excluded.gpx_content,
                uploaded_at = excluded.uploaded_at
            """;
        cmd.Parameters.AddWithValue("@code", sessionId);
        cmd.Parameters.AddWithValue("@gpx", gpxContent);
        cmd.Parameters.AddWithValue("@uploadedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public bool DeleteRoute(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM session_routes WHERE session_id = @code";
        cmd.Parameters.AddWithValue("@code", sessionId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int MergeSession(string sourceId, string targetId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE location_updates SET session_id = @tgt WHERE session_id = @src";
        cmd.Parameters.AddWithValue("@tgt", targetId);
        cmd.Parameters.AddWithValue("@src", sourceId);
        var rows = cmd.ExecuteNonQuery();

        if (_sessions.TryRemove(sourceId, out var srcSession))
        {
            var tgtSession = _sessions.GetOrAdd(targetId, _ => new());
            foreach (var (userId, srcHistory) in srcSession)
            {
                var tgtHistory = tgtSession.GetOrAdd(userId, _ => []);
                lock (srcHistory) lock (tgtHistory)
                {
                    tgtHistory.AddRange(srcHistory);
                    tgtHistory.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                }
            }
        }

        return rows;
    }

    // On conflict a rejoin refreshes the membership; preserveExistingRole keeps a role the
    // user already held (e.g. runner) from being downgraded by a plain invite-code rejoin.
    private static void UpsertMembership(
        NpgsqlConnection conn,
        string sessionId,
        string userId,
        string role,
        string displayName,
        string joinedAt,
        bool preserveExistingRole = false)
    {
        using var cmd = conn.CreateCommand();
        var roleUpdate = preserveExistingRole ? "" : "role = excluded.role,";
        cmd.CommandText = $"""
            INSERT INTO session_members (session_id, user_id, role, display_name, joined_at, left_at)
            VALUES (@sessionId, @userId, @role, @displayName, @joinedAt, NULL)
            ON CONFLICT(session_id, user_id)
            DO UPDATE SET {roleUpdate} display_name = excluded.display_name,
                          joined_at = excluded.joined_at, left_at = NULL
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@displayName", displayName);
        cmd.Parameters.AddWithValue("@joinedAt", joinedAt);
        cmd.ExecuteNonQuery();
    }

    private static IReadOnlyList<SessionMemberForAccountDeletion> GetSessionMembersForUser(
        NpgsqlConnection conn,
        string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id
            FROM session_members
            WHERE user_id = @userId
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        var memberships = new List<SessionMemberForAccountDeletion>();
        while (reader.Read())
            memberships.Add(new SessionMemberForAccountDeletion(reader.GetString(0)));
        return memberships;
    }

    private static HashSet<string> GetOwnedSessionIds(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id
            FROM app_sessions
            WHERE owner_user_id = @userId
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
            sessionIds.Add(reader.GetString(0));
        return sessionIds;
    }

    private static bool SessionExists(NpgsqlConnection conn, string sessionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1
            FROM app_sessions
            WHERE id = @sessionId
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        return cmd.ExecuteScalar() is not null;
    }

    private static void DeleteExpiredRevokedTokens(NpgsqlConnection conn, DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM revoked_user_tokens WHERE expires_at <= @now";
        cmd.Parameters.AddWithValue("@now", now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public static string? NormalizeSessionName(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 4 or > 8)
            return null;

        return code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? code
            : null;
    }

    private static string GenerateCode(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes / 2)).ToUpperInvariant();

    private sealed record SessionMemberForAccountDeletion(
        string SessionId);
}
