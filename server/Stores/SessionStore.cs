using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace DotWatcher.Server;

public class SessionStore(string dbPath)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // session code -> user id -> live position history
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RunnerPosition>>> _sessions = new();
    private readonly string _connectionString = $"Data Source={dbPath}";

    public void Initialize()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS location_updates (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                session_code TEXT NOT NULL,
                runner_user_id TEXT,
                runner_name  TEXT NOT NULL,
                latitude     REAL NOT NULL,
                longitude    REAL NOT NULL,
                heading      REAL,
                timestamp    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_session        ON location_updates(session_code);
            CREATE INDEX IF NOT EXISTS idx_session_runner ON location_updates(session_code, runner_name);

            CREATE TABLE IF NOT EXISTS users (
                id            TEXT PRIMARY KEY,
                username      TEXT NOT NULL UNIQUE,
                display_name  TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                created_at    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_sessions (
                session_code  TEXT PRIMARY KEY,
                invite_code   TEXT NOT NULL UNIQUE,
                owner_user_id TEXT NOT NULL,
                created_at    TEXT NOT NULL,
                FOREIGN KEY(owner_user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS session_members (
                session_code TEXT NOT NULL,
                user_id      TEXT NOT NULL,
                role         TEXT NOT NULL,
                display_name TEXT NOT NULL,
                joined_at    TEXT NOT NULL,
                PRIMARY KEY(session_code, user_id),
                FOREIGN KEY(session_code) REFERENCES app_sessions(session_code),
                FOREIGN KEY(user_id) REFERENCES users(id)
            );

            CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
            CREATE INDEX IF NOT EXISTS idx_app_sessions_invite ON app_sessions(invite_code);
            CREATE INDEX IF NOT EXISTS idx_session_members_user ON session_members(user_id);

            CREATE TABLE IF NOT EXISTS revoked_user_tokens (
                token_id   TEXT PRIMARY KEY,
                expires_at TEXT NOT NULL,
                revoked_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_revoked_user_tokens_expires_at ON revoked_user_tokens(expires_at);

            CREATE TABLE IF NOT EXISTS join_requests (
                id           TEXT PRIMARY KEY,
                session_code TEXT NOT NULL,
                user_id      TEXT NOT NULL,
                display_name TEXT NOT NULL,
                status       TEXT NOT NULL DEFAULT 'pending',
                created_at   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_join_requests_session ON join_requests(session_code, status);
            """;
        cmd.ExecuteNonQuery();
        AddColumnIfMissing(conn, "location_updates", "runner_user_id", "TEXT");

        using var locationRunnerUserIndex = conn.CreateCommand();
        locationRunnerUserIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_location_updates_runner_user ON location_updates(runner_user_id)
            """;
        locationRunnerUserIndex.ExecuteNonQuery();
    }

    private SqliteConnection Connect()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public bool CreateUser(UserAccount account)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO users (id, username, display_name, password_hash, created_at)
            VALUES ($id, $username, $displayName, $passwordHash, $createdAt)
            """;
        cmd.Parameters.AddWithValue("$id", account.Id);
        cmd.Parameters.AddWithValue("$username", account.Username);
        cmd.Parameters.AddWithValue("$displayName", account.DisplayName);
        cmd.Parameters.AddWithValue("$passwordHash", account.PasswordHash);
        cmd.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() == 1;
    }

    public UserAccount? GetUserByUsername(string username)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, display_name, password_hash
            FROM users
            WHERE username = $username
            """;
        cmd.Parameters.AddWithValue("$username", username);
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
            WHERE id = $userId
            """;
        cmd.Parameters.AddWithValue("$userId", userId);
        return cmd.ExecuteScalar() is not null;
    }

    public void RevokeUserToken(string tokenId, DateTimeOffset expiresAt)
    {
        using var conn = Connect();
        DeleteExpiredRevokedTokens(conn, DateTimeOffset.UtcNow);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO revoked_user_tokens (token_id, expires_at, revoked_at)
            VALUES ($tokenId, $expiresAt, $revokedAt)
            """;
        cmd.Parameters.AddWithValue("$tokenId", tokenId);
        cmd.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O"));
        cmd.Parameters.AddWithValue("$revokedAt", DateTimeOffset.UtcNow.ToString("O"));
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
            WHERE token_id = $tokenId AND expires_at > $now
            """;
        cmd.Parameters.AddWithValue("$tokenId", tokenId);
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public bool DeleteUserAccount(string userId)
    {
        using var conn = Connect();
        var memberships = GetSessionMembersForUser(conn, userId);
        var ownedSessionCodes = GetOwnedSessionCodes(conn, userId);

        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM location_updates WHERE runner_user_id = $userId";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.ExecuteNonQuery();
        }

        foreach (var sessionCode in ownedSessionCodes)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM location_updates WHERE session_code = $sessionCode";
                cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM session_members WHERE session_code = $sessionCode";
                cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM app_sessions WHERE owner_user_id = $userId";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM session_members WHERE user_id = $userId";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.ExecuteNonQuery();
        }

        int deletedUsers;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM users WHERE id = $userId";
            cmd.Parameters.AddWithValue("$userId", userId);
            deletedUsers = cmd.ExecuteNonQuery();
        }

        tx.Commit();

        foreach (var sessionCode in ownedSessionCodes)
            _sessions.TryRemove(sessionCode, out _);

        foreach (var membership in memberships.Where(membership => !ownedSessionCodes.Contains(membership.SessionCode)))
        {
            if (_sessions.TryGetValue(membership.SessionCode, out var session))
                session.TryRemove(userId, out _);
        }

        return deletedUsers > 0;
    }

    public SessionMembership CreateSessionForUser(string userId, string displayName, string? requestedCode = null)
    {
        var sessionCode = NormalizeSessionCode(requestedCode) ?? GenerateCode(8);
        var inviteCode = GenerateCode(12);
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();

        while (true)
        {
            using var sessionCmd = conn.CreateCommand();
            sessionCmd.CommandText = """
                INSERT OR IGNORE INTO app_sessions (session_code, invite_code, owner_user_id, created_at)
                VALUES ($sessionCode, $inviteCode, $ownerUserId, $createdAt)
                """;
            sessionCmd.Parameters.AddWithValue("$sessionCode", sessionCode);
            sessionCmd.Parameters.AddWithValue("$inviteCode", inviteCode);
            sessionCmd.Parameters.AddWithValue("$ownerUserId", userId);
            sessionCmd.Parameters.AddWithValue("$createdAt", now);

            if (sessionCmd.ExecuteNonQuery() == 1)
                break;

            if (requestedCode is not null)
                throw new InvalidOperationException("Session code is already in use.");

            sessionCode = GenerateCode(8);
            inviteCode = GenerateCode(12);
        }

        UpsertMembership(conn, sessionCode, userId, "owner", displayName, now);

        return new SessionMembership(sessionCode, inviteCode, "owner", displayName);
    }

    public SessionMembership? JoinSessionByInvite(string inviteCode, string userId, string displayName)
    {
        var normalizedInvite = NormalizeSessionCode(inviteCode);
        if (normalizedInvite is null)
            return null;

        using var conn = Connect();
        using var lookup = conn.CreateCommand();
        lookup.CommandText = """
            SELECT session_code, invite_code
            FROM app_sessions
            WHERE invite_code = $inviteCode
            """;
        lookup.Parameters.AddWithValue("$inviteCode", normalizedInvite);
        using var reader = lookup.ExecuteReader();
        if (!reader.Read())
            return null;

        var sessionCode = reader.GetString(0);
        var storedInviteCode = reader.GetString(1);
        reader.Close();

        var joinedAt = DateTimeOffset.UtcNow.ToString("O");
        UpsertMembershipPreservingRole(conn, sessionCode, userId, "viewer", displayName, joinedAt);

        return GetMembership(conn, sessionCode, userId, storedInviteCode)!;
    }

    public IReadOnlyList<SessionMembership> GetSessionsForUser(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.session_code, s.invite_code, m.role, m.display_name
            FROM session_members m
            JOIN app_sessions s ON s.session_code = m.session_code
            WHERE m.user_id = $userId
            ORDER BY m.joined_at DESC
            """;
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();

        var sessions = new List<SessionMembership>();
        while (reader.Read())
            sessions.Add(new SessionMembership(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));

        return sessions;
    }

    public SessionMembership? GetMembership(string sessionCode, string userId)
    {
        using var conn = Connect();
        return GetMembership(conn, sessionCode.ToUpperInvariant(), userId);
    }

    private static SessionMembership? GetMembership(
        SqliteConnection conn,
        string sessionCode,
        string userId,
        string? inviteCode = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.session_code, s.invite_code, m.role, m.display_name
            FROM session_members m
            JOIN app_sessions s ON s.session_code = m.session_code
            WHERE m.session_code = $sessionCode AND m.user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SessionMembership(reader.GetString(0), inviteCode ?? reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public bool CanReadSession(string sessionCode, string userId) =>
        GetMembership(sessionCode, userId) is not null;

    public bool CanWriteLocation(string sessionCode, string userId)
    {
        var membership = GetMembership(sessionCode, userId);
        return membership?.Role is "owner" or "runner";
    }

    public bool IsSessionOwner(string sessionCode, string userId) =>
        GetMembership(sessionCode, userId)?.Role == "owner";

    public IReadOnlyList<SessionMember>? GetSessionMembers(string sessionCode)
    {
        using var conn = Connect();
        if (!SessionExists(conn, sessionCode))
            return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, role, display_name
            FROM session_members
            WHERE session_code = $sessionCode
            ORDER BY joined_at ASC, display_name ASC
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        using var reader = cmd.ExecuteReader();

        var members = new List<SessionMember>();
        while (reader.Read())
            members.Add(new SessionMember(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));

        return members;
    }

    public UpdateSessionMemberRoleResult UpdateSessionMemberRole(
        string sessionCode,
        string userId,
        string role)
    {
        if (role is not ("runner" or "viewer"))
            throw new ArgumentOutOfRangeException(nameof(role), "Role must be runner or viewer.");

        using var conn = Connect();
        if (!SessionExists(conn, sessionCode))
            return new UpdateSessionMemberRoleResult(UpdateSessionMemberRoleStatus.SessionNotFound, null);

        var existing = GetSessionMember(conn, sessionCode, userId);
        if (existing is null)
            return new UpdateSessionMemberRoleResult(UpdateSessionMemberRoleStatus.MemberNotFound, null);

        if (existing.Role == "owner")
            return new UpdateSessionMemberRoleResult(UpdateSessionMemberRoleStatus.OwnerRoleImmutable, existing);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE session_members
            SET role = $role
            WHERE session_code = $sessionCode AND user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.ExecuteNonQuery();

        return new UpdateSessionMemberRoleResult(
            UpdateSessionMemberRoleStatus.Updated,
            existing with { Role = role });
    }

    public void AddPosition(ValidatedLocationUpdate update, string userId)
    {
        var code = update.SessionCode;

        var session = _sessions.GetOrAdd(code, _ => new());
        var history = session.GetOrAdd(userId, _ => []);
        lock (history)
            history.Add(new RunnerPosition(
                update.RunnerName,
                update.Latitude,
                update.Longitude,
                update.Heading,
                update.Timestamp));

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_code, runner_user_id, runner_name, latitude, longitude, heading, timestamp)
            VALUES ($code, $userId, $name, $lat, $lon, $heading, $ts)
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$name", update.RunnerName);
        cmd.Parameters.AddWithValue("$lat", update.Latitude);
        cmd.Parameters.AddWithValue("$lon", update.Longitude);
        cmd.Parameters.AddWithValue("$heading", update.Heading.HasValue ? (object)update.Heading.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", update.Timestamp.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetParticipants(string sessionCode)
    {
        if (!_sessions.TryGetValue(sessionCode.ToUpperInvariant(), out var session))
            return [];
        var participants = new List<string>(session.Count);
        foreach (var (_, history) in session)
        {
            lock (history)
            {
                if (history.Count > 0)
                    participants.Add(history[^1].RunnerName);
            }
        }

        return participants;
    }

    public IReadOnlyList<RunnerPosition[]> GetLatestPositions(string sessionCode)
    {
        if (!_sessions.TryGetValue(sessionCode.ToUpperInvariant(), out var session))
            return [];

        var result = new List<RunnerPosition[]>(session.Count);
        foreach (var (_, history) in session)
        {
            lock (history)
            {
                if (history.Count > 0)
                    result.Add(history.TakeLast(1).ToArray());
            }
        }
        return result;
    }

    public IReadOnlyList<AdminSessionSummary> GetAllSessions()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.session_code, u.username, COUNT(m.user_id) AS member_count, s.created_at
            FROM app_sessions s
            JOIN users u ON u.id = s.owner_user_id
            LEFT JOIN session_members m ON m.session_code = s.session_code
            GROUP BY s.session_code
            ORDER BY s.created_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        var sessions = new List<AdminSessionSummary>();
        while (reader.Read())
            sessions.Add(new AdminSessionSummary(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        return sessions;
    }

    public IReadOnlyList<AdminJoinRequestSummary> GetAllJoinRequests()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT jr.id, jr.session_code, u.username, jr.display_name, jr.status, jr.created_at
            FROM join_requests jr
            JOIN users u ON u.id = jr.user_id
            ORDER BY jr.created_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        var requests = new List<AdminJoinRequestSummary>();
        while (reader.Read())
            requests.Add(new AdminJoinRequestSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        return requests;
    }

    public IReadOnlyList<BrowsableSession> GetBrowsableSessions()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24).ToString("O");
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.session_code, u.display_name, COUNT(DISTINCT m.user_id) AS member_count,
                COALESCE(
                    (SELECT MAX(l.timestamp) FROM location_updates l WHERE l.session_code = s.session_code),
                    s.created_at
                ) AS last_activity
            FROM app_sessions s
            JOIN users u ON u.id = s.owner_user_id
            LEFT JOIN session_members m ON m.session_code = s.session_code
            WHERE s.created_at > $cutoff
               OR EXISTS (SELECT 1 FROM location_updates l WHERE l.session_code = s.session_code AND l.timestamp > $cutoff)
            GROUP BY s.session_code
            ORDER BY last_activity DESC
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        using var reader = cmd.ExecuteReader();
        var sessions = new List<BrowsableSession>();
        while (reader.Read())
            sessions.Add(new BrowsableSession(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        return sessions;
    }

    public CreateJoinRequestResult CreateJoinRequest(string sessionCode, string userId, string displayName)
    {
        using var conn = Connect();

        if (!SessionExists(conn, sessionCode))
            return new CreateJoinRequestResult(CreateJoinRequestStatus.SessionNotFound, null);

        var membership = GetMembership(conn, sessionCode, userId);
        if (membership?.Role == "owner")
            return new CreateJoinRequestResult(CreateJoinRequestStatus.OwnSession, null);
        if (membership is not null)
            return new CreateJoinRequestResult(CreateJoinRequestStatus.AlreadyMember, null);

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = """
            SELECT id, session_code, user_id, display_name, created_at
            FROM join_requests
            WHERE session_code = $sessionCode AND user_id = $userId AND status = 'pending'
            """;
        checkCmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        checkCmd.Parameters.AddWithValue("$userId", userId);
        using var checkReader = checkCmd.ExecuteReader();
        if (checkReader.Read())
        {
            var existing = new JoinRequestRecord(
                checkReader.GetString(0),
                checkReader.GetString(1),
                checkReader.GetString(2),
                checkReader.GetString(3),
                DateTimeOffset.Parse(checkReader.GetString(4)));
            return new CreateJoinRequestResult(CreateJoinRequestStatus.AlreadyPending, existing);
        }
        checkReader.Close();

        var requestId = GenerateCode(8);
        var createdAt = DateTimeOffset.UtcNow;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO join_requests (id, session_code, user_id, display_name, status, created_at)
            VALUES ($id, $sessionCode, $userId, $displayName, 'pending', $createdAt)
            """;
        cmd.Parameters.AddWithValue("$id", requestId);
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        cmd.ExecuteNonQuery();

        return new CreateJoinRequestResult(
            CreateJoinRequestStatus.Created,
            new JoinRequestRecord(requestId, sessionCode, userId, displayName, createdAt));
    }

    public IReadOnlyList<JoinRequestRecord>? GetJoinRequests(string sessionCode)
    {
        using var conn = Connect();
        if (!SessionExists(conn, sessionCode))
            return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_code, user_id, display_name, created_at
            FROM join_requests
            WHERE session_code = $sessionCode AND status = 'pending'
            ORDER BY created_at ASC
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        using var reader = cmd.ExecuteReader();

        var requests = new List<JoinRequestRecord>();
        while (reader.Read())
            requests.Add(new JoinRequestRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        return requests;
    }

    public int GetPendingJoinRequestCount(string sessionCode)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(1)
            FROM join_requests
            WHERE session_code = $sessionCode AND status = 'pending'
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    public SessionMembership? ApproveJoinRequest(string requestId, string sessionCode)
    {
        using var conn = Connect();

        string userId, displayName;
        using (var findCmd = conn.CreateCommand())
        {
            findCmd.CommandText = """
                SELECT user_id, display_name
                FROM join_requests
                WHERE id = $requestId AND session_code = $sessionCode AND status = 'pending'
                """;
            findCmd.Parameters.AddWithValue("$requestId", requestId);
            findCmd.Parameters.AddWithValue("$sessionCode", sessionCode);
            using var reader = findCmd.ExecuteReader();
            if (!reader.Read())
                return null;
            userId = reader.GetString(0);
            displayName = reader.GetString(1);
        }

        using var tx = conn.BeginTransaction();

        using (var updateCmd = conn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE join_requests SET status = 'approved' WHERE id = $requestId";
            updateCmd.Parameters.AddWithValue("$requestId", requestId);
            updateCmd.ExecuteNonQuery();
        }

        var joinedAt = DateTimeOffset.UtcNow.ToString("O");
        using (var memberCmd = conn.CreateCommand())
        {
            memberCmd.Transaction = tx;
            memberCmd.CommandText = """
                INSERT INTO session_members (session_code, user_id, role, display_name, joined_at)
                VALUES ($sessionCode, $userId, 'viewer', $displayName, $joinedAt)
                ON CONFLICT(session_code, user_id) DO UPDATE SET display_name = excluded.display_name
                """;
            memberCmd.Parameters.AddWithValue("$sessionCode", sessionCode);
            memberCmd.Parameters.AddWithValue("$userId", userId);
            memberCmd.Parameters.AddWithValue("$displayName", displayName);
            memberCmd.Parameters.AddWithValue("$joinedAt", joinedAt);
            memberCmd.ExecuteNonQuery();
        }

        tx.Commit();

        return GetMembership(conn, sessionCode, userId);
    }

    public bool DenyJoinRequest(string requestId, string sessionCode)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE join_requests SET status = 'denied'
            WHERE id = $requestId AND session_code = $sessionCode AND status = 'pending'
            """;
        cmd.Parameters.AddWithValue("$requestId", requestId);
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteSession(string sessionCode)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        foreach (var table in new[] { "join_requests", "location_updates", "session_members" })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE session_code = $code";
            cmd.Parameters.AddWithValue("$code", sessionCode);
            cmd.ExecuteNonQuery();
        }

        int rows;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM app_sessions WHERE session_code = $code";
            cmd.Parameters.AddWithValue("$code", sessionCode);
            rows = cmd.ExecuteNonQuery();
        }

        tx.Commit();
        _sessions.TryRemove(sessionCode, out _);
        return rows > 0;
    }

    public IEnumerable<string> GetRecordedSessions()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_code
            FROM location_updates
            GROUP BY session_code
            ORDER BY MAX(timestamp) DESC
            """;
        using var reader = cmd.ExecuteReader();
        var codes = new List<string>();
        while (reader.Read())
            codes.Add(reader.GetString(0));
        return codes;
    }

    public bool HasRecording(string sessionCode)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM location_updates WHERE session_code = $code";
        cmd.Parameters.AddWithValue("$code", sessionCode.ToUpperInvariant());
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public string GetRecordingAsNdjson(string sessionCode)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_code, runner_name, latitude, longitude, heading, timestamp
            FROM location_updates
            WHERE session_code = $code
            ORDER BY id
            """;
        cmd.Parameters.AddWithValue("$code", sessionCode.ToUpperInvariant());
        using var reader = cmd.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
        {
            var update = new LocationUpdate(
                RunnerName: reader.GetString(1),
                SessionCode: reader.GetString(0),
                Latitude: reader.GetDouble(2),
                Longitude: reader.GetDouble(3),
                Heading: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                Timestamp: DateTimeOffset.Parse(reader.GetString(5))
            );
            lines.Add(JsonSerializer.Serialize(update, _jsonOptions));
        }
        return string.Join("\n", lines);
    }

    public void SaveRecording(string sessionCode, string ndjsonContent)
    {
        var code = NormalizeSessionCode(sessionCode);
        if (code is null)
            throw new LocationUpdateValidationException(["Session code must be 3-32 letters, numbers, dashes, or underscores."]);

        var lines = ndjsonContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var updates = lines
            .Select((line, index) => ParseRecordingLine(line, code, index + 1))
            .ToList();

        if (updates.Count == 0)
            throw new LocationUpdateValidationException(["Recording must contain at least one location update."]);

        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM location_updates WHERE session_code = $code";
            del.Parameters.AddWithValue("$code", code);
            del.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_code, runner_user_id, runner_name, latitude, longitude, heading, timestamp)
            VALUES ($code, NULL, $name, $lat, $lon, $heading, $ts)
            """;
        var pCode    = cmd.Parameters.Add("$code",    SqliteType.Text);
        var pName    = cmd.Parameters.Add("$name",    SqliteType.Text);
        var pLat     = cmd.Parameters.Add("$lat",     SqliteType.Real);
        var pLon     = cmd.Parameters.Add("$lon",     SqliteType.Real);
        var pHeading = cmd.Parameters.Add("$heading", SqliteType.Real);
        var pTs      = cmd.Parameters.Add("$ts",      SqliteType.Text);

        foreach (var u in updates)
        {
            pCode.Value    = code;
            pName.Value    = u.RunnerName;
            pLat.Value     = u.Latitude;
            pLon.Value     = u.Longitude;
            pHeading.Value = u.Heading.HasValue ? (object)u.Heading.Value : DBNull.Value;
            pTs.Value      = u.Timestamp.ToString("O");
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static ValidatedLocationUpdate ParseRecordingLine(string line, string sessionCode, int lineNumber)
    {
        var parsed = JsonSerializer.Deserialize<LocationUpdate>(line, _jsonOptions)
            ?? throw new LocationUpdateValidationException([$"Line {lineNumber}: location update is required."]);

        var update = parsed with { SessionCode = sessionCode };
        if (!LocationUpdateValidation.TryValidate(update, out var validated, out var errors))
            throw new LocationUpdateValidationException(
                errors.Select(error => $"Line {lineNumber}: {error}").ToList());

        return validated;
    }

    public bool DeleteRecording(string sessionCode)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM location_updates WHERE session_code = $code";
        cmd.Parameters.AddWithValue("$code", sessionCode.ToUpperInvariant());
        return cmd.ExecuteNonQuery() > 0;
    }

    public void ClearSession(string sessionCode) =>
        _sessions.TryRemove(sessionCode.ToUpperInvariant(), out _);

    public int MergeSession(string sourceCode, string targetCode)
    {
        var src = sourceCode.ToUpperInvariant();
        var tgt = targetCode.ToUpperInvariant();

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE location_updates SET session_code = $tgt WHERE session_code = $src";
        cmd.Parameters.AddWithValue("$tgt", tgt);
        cmd.Parameters.AddWithValue("$src", src);
        var rows = cmd.ExecuteNonQuery();

        if (_sessions.TryRemove(src, out var srcSession))
        {
            var tgtSession = _sessions.GetOrAdd(tgt, _ => new());
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

    private static void UpsertMembership(
        SqliteConnection conn,
        string sessionCode,
        string userId,
        string role,
        string displayName,
        string joinedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_members (session_code, user_id, role, display_name, joined_at)
            VALUES ($sessionCode, $userId, $role, $displayName, $joinedAt)
            ON CONFLICT(session_code, user_id)
            DO UPDATE SET role = excluded.role, display_name = excluded.display_name
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$joinedAt", joinedAt);
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(
        SqliteConnection conn,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
        alter.ExecuteNonQuery();
    }

    private static IReadOnlyList<SessionMemberForAccountDeletion> GetSessionMembersForUser(
        SqliteConnection conn,
        string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_code
            FROM session_members
            WHERE user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        var memberships = new List<SessionMemberForAccountDeletion>();
        while (reader.Read())
            memberships.Add(new SessionMemberForAccountDeletion(reader.GetString(0)));
        return memberships;
    }

    private static HashSet<string> GetOwnedSessionCodes(SqliteConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_code
            FROM app_sessions
            WHERE owner_user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        var sessionCodes = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
            sessionCodes.Add(reader.GetString(0));
        return sessionCodes;
    }

    private static bool SessionExists(SqliteConnection conn, string sessionCode)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1
            FROM app_sessions
            WHERE session_code = $sessionCode
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        return cmd.ExecuteScalar() is not null;
    }

    private static SessionMember? GetSessionMember(
        SqliteConnection conn,
        string sessionCode,
        string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_id, role, display_name
            FROM session_members
            WHERE session_code = $sessionCode AND user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SessionMember(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static void UpsertMembershipPreservingRole(
        SqliteConnection conn,
        string sessionCode,
        string userId,
        string role,
        string displayName,
        string joinedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_members (session_code, user_id, role, display_name, joined_at)
            VALUES ($sessionCode, $userId, $role, $displayName, $joinedAt)
            ON CONFLICT(session_code, user_id)
            DO UPDATE SET display_name = excluded.display_name
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$joinedAt", joinedAt);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteExpiredRevokedTokens(SqliteConnection conn, DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM revoked_user_tokens WHERE expires_at <= $now";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public static string? NormalizeSessionCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 3 or > 32)
            return null;

        return code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? code
            : null;
    }

    private static string GenerateCode(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes / 2)).ToUpperInvariant();

    private sealed record SessionMemberForAccountDeletion(
        string SessionCode);
}
