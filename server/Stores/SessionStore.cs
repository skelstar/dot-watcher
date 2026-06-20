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
            """;
        cmd.ExecuteNonQuery();
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

        UpsertMembership(
            conn,
            sessionCode,
            userId,
            "viewer",
            displayName,
            DateTimeOffset.UtcNow.ToString("O"));

        return new SessionMembership(sessionCode, storedInviteCode, "viewer", displayName);
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.session_code, s.invite_code, m.role, m.display_name
            FROM session_members m
            JOIN app_sessions s ON s.session_code = m.session_code
            WHERE m.session_code = $sessionCode AND m.user_id = $userId
            """;
        cmd.Parameters.AddWithValue("$sessionCode", sessionCode.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SessionMembership(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public bool CanReadSession(string sessionCode, string userId) =>
        GetMembership(sessionCode, userId) is not null;

    public bool CanWriteLocation(string sessionCode, string userId)
    {
        var membership = GetMembership(sessionCode, userId);
        return membership?.Role is "owner" or "runner";
    }

    public void AddPosition(ValidatedLocationUpdate update)
    {
        var code = update.SessionCode;

        var session = _sessions.GetOrAdd(code, _ => new());
        var history = session.GetOrAdd(update.RunnerName, _ => []);
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
            INSERT INTO location_updates (session_code, runner_name, latitude, longitude, heading, timestamp)
            VALUES ($code, $name, $lat, $lon, $heading, $ts)
            """;
        cmd.Parameters.AddWithValue("$code", code);
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
        return session.Keys.ToList();
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
            INSERT INTO location_updates (session_code, runner_name, latitude, longitude, heading, timestamp)
            VALUES ($code, $name, $lat, $lon, $heading, $ts)
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
            foreach (var (runner, srcHistory) in srcSession)
            {
                var tgtHistory = tgtSession.GetOrAdd(runner, _ => []);
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
}
