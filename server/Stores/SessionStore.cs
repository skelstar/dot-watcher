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

    // session id -> user id -> live position history
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RunnerPosition>>> _sessions = new();
    private readonly string _connectionString = $"Data Source={dbPath}";

    public void Initialize()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS location_updates (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id     TEXT NOT NULL,
                runner_user_id TEXT,
                runner_name    TEXT NOT NULL,
                latitude       REAL NOT NULL,
                longitude      REAL NOT NULL,
                heading        REAL,
                timestamp      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_session        ON location_updates(session_id);
            CREATE INDEX IF NOT EXISTS idx_session_runner ON location_updates(session_id, runner_name);

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
                FOREIGN KEY(owner_user_id) REFERENCES users(id)
            );
            CREATE INDEX IF NOT EXISTS idx_app_sessions_invite ON app_sessions(invite_code);

            CREATE TABLE IF NOT EXISTS session_members (
                session_id   TEXT NOT NULL,
                user_id      TEXT NOT NULL,
                role         TEXT NOT NULL,
                display_name TEXT NOT NULL,
                joined_at    TEXT NOT NULL,
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
            """;
        cmd.ExecuteNonQuery();

        // Migration: drop the retired join-request feature's table (invite codes are now the only join path)
        using (var dropJoinRequests = conn.CreateCommand())
        {
            dropJoinRequests.CommandText = "DROP TABLE IF EXISTS join_requests";
            dropJoinRequests.ExecuteNonQuery();
        }

        // Migration: session_code → session_id rename + session_name column (commit 6965edb)
        // If app_sessions still has the old session_code primary key, drop and recreate all
        // session-related tables. Users and revoked tokens are preserved.
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('app_sessions') WHERE name = 'session_code'";
            var hasOldSchema = (long)(check.ExecuteScalar() ?? 0L) > 0;
            if (hasOldSchema)
            {
                using var drop = conn.CreateCommand();
                drop.CommandText = """
                    DROP TABLE IF EXISTS join_requests;
                    DROP TABLE IF EXISTS session_members;
                    DROP TABLE IF EXISTS location_updates;
                    DROP TABLE IF EXISTS app_sessions;
                    CREATE TABLE location_updates (
                        id             INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id     TEXT NOT NULL,
                        runner_user_id TEXT,
                        runner_name    TEXT NOT NULL,
                        latitude       REAL NOT NULL,
                        longitude      REAL NOT NULL,
                        heading        REAL,
                        timestamp      TEXT NOT NULL
                    );
                    CREATE INDEX idx_session        ON location_updates(session_id);
                    CREATE INDEX idx_session_runner ON location_updates(session_id, runner_name);
                    CREATE TABLE app_sessions (
                        id            TEXT PRIMARY KEY,
                        session_name  TEXT NOT NULL,
                        invite_code   TEXT NOT NULL UNIQUE,
                        owner_user_id TEXT NOT NULL,
                        created_at    TEXT NOT NULL,
                        FOREIGN KEY(owner_user_id) REFERENCES users(id)
                    );
                    CREATE INDEX idx_app_sessions_invite ON app_sessions(invite_code);
                    CREATE TABLE session_members (
                        session_id   TEXT NOT NULL,
                        user_id      TEXT NOT NULL,
                        role         TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        joined_at    TEXT NOT NULL,
                        PRIMARY KEY(session_id, user_id),
                        FOREIGN KEY(session_id) REFERENCES app_sessions(id),
                        FOREIGN KEY(user_id)    REFERENCES users(id)
                    );
                    CREATE INDEX idx_session_members_user ON session_members(user_id);
                    """;
                drop.ExecuteNonQuery();
            }
        }
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
        var ownedSessionIds = GetOwnedSessionIds(conn, userId);

        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM location_updates WHERE runner_user_id = $userId";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.ExecuteNonQuery();
        }

        foreach (var sessionId in ownedSessionIds)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM location_updates WHERE session_id = $sessionId";
                cmd.Parameters.AddWithValue("$sessionId", sessionId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM session_members WHERE session_id = $sessionId";
                cmd.Parameters.AddWithValue("$sessionId", sessionId);
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
        dupCheck.CommandText = "SELECT 1 FROM app_sessions WHERE session_name = $name";
        dupCheck.Parameters.AddWithValue("$name", sessionName);
        if (dupCheck.ExecuteScalar() is not null)
            return null;

        using var sessionCmd = conn.CreateCommand();
        sessionCmd.CommandText = """
            INSERT INTO app_sessions (id, session_name, invite_code, owner_user_id, created_at)
            VALUES ($sessionId, $sessionName, $inviteCode, $ownerUserId, $createdAt)
            """;
        sessionCmd.Parameters.AddWithValue("$sessionId", sessionId);
        sessionCmd.Parameters.AddWithValue("$sessionName", sessionName);
        sessionCmd.Parameters.AddWithValue("$inviteCode", inviteCode);
        sessionCmd.Parameters.AddWithValue("$ownerUserId", userId);
        sessionCmd.Parameters.AddWithValue("$createdAt", now);
        sessionCmd.ExecuteNonQuery();

        UpsertMembership(conn, sessionId, userId, "runner", displayName, now);

        return new SessionMembership(sessionId, sessionName, inviteCode, "runner", displayName);
    }

    public SessionMembership? JoinSessionByInvite(string inviteCode, string userId, string displayName, string role = "viewer")
    {
        var normalizedInvite = NormalizeSessionName(inviteCode);
        if (normalizedInvite is null)
            return null;

        using var conn = Connect();
        using var lookup = conn.CreateCommand();
        lookup.CommandText = """
            SELECT id, session_name, invite_code
            FROM app_sessions
            WHERE invite_code = $inviteCode
            """;
        lookup.Parameters.AddWithValue("$inviteCode", normalizedInvite);
        using var reader = lookup.ExecuteReader();
        if (!reader.Read())
            return null;

        var sessionId = reader.GetString(0);
        var storedInviteCode = reader.GetString(2);
        reader.Close();

        var joinedAt = DateTimeOffset.UtcNow.ToString("O");
        UpsertMembershipPreservingRole(conn, sessionId, userId, role, displayName, joinedAt);

        return GetMembership(conn, sessionId, userId, storedInviteCode)!;
    }

    public string? GetSessionIdByInviteCode(string inviteCode)
    {
        var normalizedInvite = NormalizeSessionName(inviteCode);
        if (normalizedInvite is null)
            return null;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM app_sessions WHERE invite_code = $inviteCode";
        cmd.Parameters.AddWithValue("$inviteCode", normalizedInvite);
        return cmd.ExecuteScalar() as string;
    }

    public IReadOnlyList<SessionMembership> GetSessionsForUser(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name
            FROM session_members m
            JOIN app_sessions s ON s.id = m.session_id
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
                reader.GetString(3),
                reader.GetString(4)));

        return sessions;
    }

    public SessionMembership? GetMembership(string sessionId, string userId)
    {
        using var conn = Connect();
        return GetMembership(conn, sessionId, userId);
    }

    private static SessionMembership? GetMembership(
        SqliteConnection conn,
        string sessionId,
        string userId,
        string? inviteCode = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name
            FROM session_members m
            JOIN app_sessions s ON s.id = m.session_id
            WHERE m.session_id = $sessionId AND m.user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SessionMembership(reader.GetString(0), reader.GetString(1), inviteCode ?? reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    public bool CanReadSession(string sessionId, string userId) =>
        GetMembership(sessionId, userId) is not null;

    public bool CanWriteLocation(string sessionId, string userId)
    {
        var membership = GetMembership(sessionId, userId);
        return membership?.Role == "runner";
    }

    public IReadOnlyList<string> GetSessionRunners(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT display_name
            FROM session_members
            WHERE session_id = $sessionId AND role = 'runner'
            ORDER BY joined_at ASC
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
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
                update.Timestamp));

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp)
            VALUES ($sessionId, $userId, $name, $lat, $lon, $heading, $ts)
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$name", update.RunnerName);
        cmd.Parameters.AddWithValue("$lat", update.Latitude);
        cmd.Parameters.AddWithValue("$lon", update.Longitude);
        cmd.Parameters.AddWithValue("$heading", update.Heading.HasValue ? (object)update.Heading.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", update.Timestamp.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetParticipants(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
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

    public IReadOnlyList<RunnerPosition[]> GetLatestPositions(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
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
            SELECT s.id, s.session_name, u.username, COUNT(m.user_id) AS member_count, s.created_at
            FROM app_sessions s
            JOIN users u ON u.id = s.owner_user_id
            LEFT JOIN session_members m ON m.session_id = s.id
            GROUP BY s.id
            ORDER BY s.created_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        var sessions = new List<AdminSessionSummary>();
        while (reader.Read())
            sessions.Add(new AdminSessionSummary(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4)));
        return sessions;
    }

    public bool LeaveSession(string sessionId, string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM session_members
            WHERE session_id = $sessionId AND user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$userId", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteSession(string sessionId)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        foreach (var table in new[] { "location_updates", "session_members" })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE session_id = $sessionId";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.ExecuteNonQuery();
        }

        int rows;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM app_sessions WHERE id = $sessionId";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
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
        cmd.CommandText = "SELECT COUNT(1) FROM location_updates WHERE session_id = $code";
        cmd.Parameters.AddWithValue("$code", sessionId);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    public string GetRecordingAsNdjson(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, runner_name, latitude, longitude, heading, timestamp
            FROM location_updates
            WHERE session_id = $code
            ORDER BY id
            """;
        cmd.Parameters.AddWithValue("$code", sessionId);
        using var reader = cmd.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
        {
            var update = new LocationUpdate(
                RunnerName: reader.GetString(1),
                SessionId: reader.GetString(0),
                Latitude: reader.GetDouble(2),
                Longitude: reader.GetDouble(3),
                Heading: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                Timestamp: DateTimeOffset.Parse(reader.GetString(5))
            );
            lines.Add(JsonSerializer.Serialize(update, _jsonOptions));
        }
        return string.Join("\n", lines);
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
            del.CommandText = "DELETE FROM location_updates WHERE session_id = $code";
            del.Parameters.AddWithValue("$code", sessionId);
            del.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp)
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
            pCode.Value    = sessionId;
            pName.Value    = u.RunnerName;
            pLat.Value     = u.Latitude;
            pLon.Value     = u.Longitude;
            pHeading.Value = u.Heading.HasValue ? (object)u.Heading.Value : DBNull.Value;
            pTs.Value      = u.Timestamp.ToString("O");
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
            LEFT JOIN location_updates l ON l.session_id = $sessionId AND l.runner_user_id = m.user_id
            WHERE m.session_id = $sessionId
            GROUP BY m.user_id, m.display_name, m.role
            ORDER BY m.joined_at ASC
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
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
            WHERE session_id = $sessionId
            ORDER BY id DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$limit", limit);
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
        cmd.CommandText = "DELETE FROM location_updates WHERE session_id = $code";
        cmd.Parameters.AddWithValue("$code", sessionId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void ClearSession(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    public int MergeSession(string sourceId, string targetId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE location_updates SET session_id = $tgt WHERE session_id = $src";
        cmd.Parameters.AddWithValue("$tgt", targetId);
        cmd.Parameters.AddWithValue("$src", sourceId);
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

    private static void UpsertMembership(
        SqliteConnection conn,
        string sessionId,
        string userId,
        string role,
        string displayName,
        string joinedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_members (session_id, user_id, role, display_name, joined_at)
            VALUES ($sessionId, $userId, $role, $displayName, $joinedAt)
            ON CONFLICT(session_id, user_id)
            DO UPDATE SET role = excluded.role, display_name = excluded.display_name
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$joinedAt", joinedAt);
        cmd.ExecuteNonQuery();
    }

    private static IReadOnlyList<SessionMemberForAccountDeletion> GetSessionMembersForUser(
        SqliteConnection conn,
        string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id
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

    private static HashSet<string> GetOwnedSessionIds(SqliteConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id
            FROM app_sessions
            WHERE owner_user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
            sessionIds.Add(reader.GetString(0));
        return sessionIds;
    }

    private static bool SessionExists(SqliteConnection conn, string sessionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1
            FROM app_sessions
            WHERE id = $sessionId
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        return cmd.ExecuteScalar() is not null;
    }

    private static void UpsertMembershipPreservingRole(
        SqliteConnection conn,
        string sessionId,
        string userId,
        string role,
        string displayName,
        string joinedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_members (session_id, user_id, role, display_name, joined_at)
            VALUES ($sessionId, $userId, $role, $displayName, $joinedAt)
            ON CONFLICT(session_id, user_id)
            DO UPDATE SET display_name = excluded.display_name
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
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
