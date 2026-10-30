using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace DotWatcher.Server;

public partial class SessionStore(string connectionString) : IDisposable
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

        // Migration: add columns for two self-reported, previously in-memory-only fields (see
        // RunnerPosition.cs) so they survive into recordings/replay, not just live viewing.
        // NOT NULL DEFAULT FALSE backfills every pre-existing row to false, which is correct -
        // none of them could have reported satellite before this column existed. Both are fast,
        // metadata-only ALTERs on Postgres 11+ (no table rewrite), safe to re-run on every startup.
        using var addLiveOnlyColumns = conn.CreateCommand();
        addLiveOnlyColumns.CommandText = """
            ALTER TABLE location_updates
                ADD COLUMN IF NOT EXISTS is_ultra_constrained BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE location_updates
                ADD COLUMN IF NOT EXISTS next_expected_at TEXT;
            """;
        addLiveOnlyColumns.ExecuteNonQuery();

        // Nullable, no default - absent means "not reported" (older client, Android, battery
        // monitoring unavailable), never a falsy stand-in for 0%. See
        // .ai/plans/battery-level-reporting.md.
        using var addBatteryLevelColumn = conn.CreateCommand();
        addBatteryLevelColumn.CommandText = """
            ALTER TABLE location_updates
                ADD COLUMN IF NOT EXISTS battery_level SMALLINT;
            """;
        addBatteryLevelColumn.ExecuteNonQuery();

        // Nullable: pre-existing sessions, and sessions created by a client that doesn't send
        // this yet, fall back to DefaultMaxLengthHours in GetRunStartTimestamp.
        using var addMaxLengthColumn = conn.CreateCommand();
        addMaxLengthColumn.CommandText = """
            ALTER TABLE app_sessions
                ADD COLUMN IF NOT EXISTS max_length_hours SMALLINT;
            """;
        addMaxLengthColumn.ExecuteNonQuery();

        // Nearby join (see .ai/plans/nearby-join.md). Additive and defaulted/nullable: Staging and
        // Production share this database and an older build names its columns explicitly, so it
        // never sees these. anchor_* is the meeting point, never returned to clients.
        using var addNearbyColumns = conn.CreateCommand();
        addNearbyColumns.CommandText = """
            ALTER TABLE app_sessions ADD COLUMN IF NOT EXISTS nearby BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE app_sessions ADD COLUMN IF NOT EXISTS nearby_radius_m SMALLINT;
            ALTER TABLE app_sessions ADD COLUMN IF NOT EXISTS anchor_lat DOUBLE PRECISION;
            ALTER TABLE app_sessions ADD COLUMN IF NOT EXISTS anchor_lon DOUBLE PRECISION;
            ALTER TABLE app_sessions ADD COLUMN IF NOT EXISTS anchor_accuracy_m REAL;
            """;
        addNearbyColumns.ExecuteNonQuery();

        // Crew (see .ai/plans/crew-management.md). Every foreign key cascades on purpose: Staging
        // and Production share this database, and a Production build that predates crew still does
        // a plain DELETE FROM users when someone deletes their account. With a non-cascading FK that
        // delete would fail for any user who has crew data. Everything here is additive: the
        // nickname column is nullable and old code names its columns explicitly, so it never sees it.
        using var addCrew = conn.CreateCommand();
        addCrew.CommandText = """
            ALTER TABLE users ADD COLUMN IF NOT EXISTS nickname TEXT;

            -- One row per mutual pair. user_low_id/user_high_id are stored in ordinal (C#) order by
            -- the application, so no ordering CHECK here: Postgres compares text by the database
            -- collation, which would not agree with string.CompareOrdinal.
            CREATE TABLE IF NOT EXISTS crew_links (
                user_low_id  TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                user_high_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                created_at   TEXT NOT NULL,
                PRIMARY KEY (user_low_id, user_high_id),
                CHECK (user_low_id <> user_high_id)
            );
            CREATE INDEX IF NOT EXISTS idx_crew_links_high ON crew_links(user_high_id);

            CREATE TABLE IF NOT EXISTS crew_codes (
                code                TEXT PRIMARY KEY,
                owner_user_id       TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                created_at          TEXT NOT NULL,
                expires_at          TEXT NOT NULL,
                redeemed_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
                redeemed_at         TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_crew_codes_owner ON crew_codes(owner_user_id);
            """;
        addCrew.ExecuteNonQuery();

        // Per-account feature flags (see .ai/plans/crew-feature-flag.md): a comma-separated list.
        // Additive with a default, so a build that predates it never sees the column.
        using var addFeatures = conn.CreateCommand();
        addFeatures.CommandText = "ALTER TABLE users ADD COLUMN IF NOT EXISTS features TEXT NOT NULL DEFAULT ''";
        addFeatures.ExecuteNonQuery();

        // Casual tracking (see .ai/plans/casual-tracking.md). Same rules as crew above: everything
        // is additive and every foreign key cascades, because Staging and Production share this
        // database and a build that predates casual still does a plain DELETE FROM users and
        // INSERTs into location_updates by explicit column list. The new columns are NOT NULL with
        // a constant default (metadata-only on Postgres 11+) so old code never has to supply them.
        using var addCasual = conn.CreateCommand();
        addCasual.CommandText = """
            ALTER TABLE users ADD COLUMN IF NOT EXISTS casual_visible_to_non_crew BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE location_updates ADD COLUMN IF NOT EXISTS in_session BOOLEAN NOT NULL DEFAULT FALSE;
            -- Which run a casual position belongs to, and when the server received it. Liveness uses
            -- the server's clock (received_at), never the phone's, which can be wrong. A null
            -- received_at marks a delayed (queued) position: stored, but not shown as live.
            ALTER TABLE location_updates ADD COLUMN IF NOT EXISTS casual_run_id TEXT;
            ALTER TABLE location_updates ADD COLUMN IF NOT EXISTS received_at TEXT;
            CREATE INDEX IF NOT EXISTS idx_location_updates_casual_run ON location_updates(casual_run_id) WHERE casual_run_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_location_updates_received ON location_updates(received_at) WHERE received_at IS NOT NULL;

            CREATE TABLE IF NOT EXISTS casual_runs (
                id         TEXT PRIMARY KEY,
                user_id    TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                started_at TEXT NOT NULL,
                ended_at   TEXT,
                start_lat  DOUBLE PRECISION,
                start_lon  DOUBLE PRECISION
            );
            ALTER TABLE casual_runs ADD COLUMN IF NOT EXISTS stopped_by_user BOOLEAN NOT NULL DEFAULT FALSE;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_casual_runs_open ON casual_runs(user_id) WHERE ended_at IS NULL;
            CREATE INDEX IF NOT EXISTS idx_casual_runs_user ON casual_runs(user_id, started_at);

            -- Secret links that let someone with no account watch one run (see .ai/plans/casual-tracking.md).
            -- Only a hash of the secret is stored. Cascades with the run, so a link goes when the run's
            -- data is purged or its owner's account is deleted.
            CREATE TABLE IF NOT EXISTS casual_watch_links (
                token_hash TEXT PRIMARY KEY,
                run_id     TEXT NOT NULL REFERENCES casual_runs(id) ON DELETE CASCADE,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_casual_watch_links_run ON casual_watch_links(run_id);
            """;
        addCasual.ExecuteNonQuery();
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

    // The DI container disposes singletons it created, so the data source's connection pool is
    // closed when the host shuts down rather than lingering until the process exits (which, in
    // tests that spin up a host per test, exhausted Postgres's connection limit).
    public void Dispose() => _dataSource.Dispose();

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
            SELECT id, username, display_name, created_at, features
            FROM users
            ORDER BY created_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        var users = new List<AdminUserSummary>();
        while (reader.Read())
            users.Add(new AdminUserSummary(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                ParseFeatures(reader.GetString(4))));
        return users;
    }

    private static IReadOnlyList<string> ParseFeatures(string stored) =>
        FeatureFlags.Normalize(stored.Split(',', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The features granted to this account itself (not counting any enabled for everyone).</summary>
    public IReadOnlyList<string> GetUserFeatures(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT features FROM users WHERE id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        return ParseFeatures(cmd.ExecuteScalar() as string ?? "");
    }

    /// <summary>Replaces the account's granted features. False when the account doesn't exist.</summary>
    public bool SetUserFeatures(string userId, IReadOnlyList<string> features)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET features = @features WHERE id = @userId";
        cmd.Parameters.AddWithValue("@features", string.Join(',', features));
        cmd.Parameters.AddWithValue("@userId", userId);
        return cmd.ExecuteNonQuery() > 0;
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

        // The casual session's owner is a system account; deleting it would delete the session and
        // every position in it.
        if (ownedSessionIds.Contains(CasualSessionId))
            return false;

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

        ForgetCasualUser(userId);

        foreach (var sessionId in ownedSessionIds)
            _sessions.TryRemove(sessionId, out _);

        foreach (var membership in memberships.Where(membership => !ownedSessionIds.Contains(membership.SessionId)))
        {
            if (_sessions.TryGetValue(membership.SessionId, out var session))
                session.TryRemove(userId, out _);
        }

        return deletedUsers > 0;
    }

    public SessionMembership CreateSessionForUser(
        string userId, string displayName, string? requestedName = null, int? maxLengthHours = null,
        NearbySettings? nearby = null)
    {
        // A nearby session's title is public text and keeps its case; the legacy name is upper-cased.
        // Neither is the invite code, which is always generated below.
        var sessionName = nearby is not null
            ? NearbyJoin.NormalizeTitle(requestedName) ?? "Session"
            : NormalizeSessionName(requestedName) ?? requestedName?.Trim() ?? "Session";
        var sessionId = Guid.NewGuid().ToString();
        var inviteCode = GenerateCode(6);
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();

        using var sessionCmd = conn.CreateCommand();
        sessionCmd.CommandText = """
            INSERT INTO app_sessions (id, session_name, invite_code, owner_user_id, created_at, max_length_hours,
                                      nearby, nearby_radius_m, anchor_lat, anchor_lon, anchor_accuracy_m)
            VALUES (@sessionId, @sessionName, @inviteCode, @ownerUserId, @createdAt, @maxLengthHours,
                    @nearby, @radius, @anchorLat, @anchorLon, @anchorAccuracy)
            """;
        sessionCmd.Parameters.AddWithValue("@nearby", nearby is not null);
        sessionCmd.Parameters.AddWithValue("@radius", nearby is null ? DBNull.Value : (short)nearby.RadiusMeters);
        sessionCmd.Parameters.AddWithValue("@anchorLat", nearby is null ? DBNull.Value : nearby.Latitude);
        sessionCmd.Parameters.AddWithValue("@anchorLon", nearby is null ? DBNull.Value : nearby.Longitude);
        sessionCmd.Parameters.AddWithValue("@anchorAccuracy",
            nearby?.AccuracyMeters is double accuracy ? (float)accuracy : DBNull.Value);
        sessionCmd.Parameters.AddWithValue("@sessionId", sessionId);
        sessionCmd.Parameters.AddWithValue("@sessionName", sessionName);
        sessionCmd.Parameters.AddWithValue("@inviteCode", inviteCode);
        sessionCmd.Parameters.AddWithValue("@ownerUserId", userId);
        sessionCmd.Parameters.AddWithValue("@createdAt", now);
        sessionCmd.Parameters.AddWithValue("@maxLengthHours", (object?)maxLengthHours ?? DBNull.Value);
        sessionCmd.ExecuteNonQuery();

        UpsertMembership(conn, sessionId, userId, "runner", displayName, now);

        return new SessionMembership(sessionId, sessionName, inviteCode, "runner", displayName, displayName, now);
    }

    /// <summary>
    /// The organiser's "Ping": moves the meeting point and/or changes the nearby switch and radius.
    /// A session that isn't the caller's, doesn't exist or is the casual session all report NotOwner, so
    /// ids can't be probed. Switching off keeps existing members; it only hides the session from discovery.
    /// </summary>
    public (NearbyUpdateOutcome Outcome, NearbyStatus? Status) UpdateNearby(
        string sessionId, string userId, bool? enabled, int? radiusMeters, MeetingPoint? point)
    {
        if (IsCasualSession(sessionId))
            return (NearbyUpdateOutcome.NotOwner, null);

        using var conn = Connect();
        using var read = conn.CreateCommand();
        read.CommandText = """
            SELECT owner_user_id, archived_at, nearby, nearby_radius_m
            FROM app_sessions
            WHERE id = @sessionId
            """;
        read.Parameters.AddWithValue("@sessionId", sessionId);

        bool wasOn;
        int currentRadius;
        using (var reader = read.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != userId)
                return (NearbyUpdateOutcome.NotOwner, null);

            if (!reader.IsDBNull(1))
                return (NearbyUpdateOutcome.Archived, null);

            wasOn = reader.GetBoolean(2);
            currentRadius = reader.IsDBNull(3) ? NearbyJoin.DefaultRadiusMeters : reader.GetInt16(3);
        }

        // Giving a radius to a session that has nearby off turns it on, as the plan describes.
        var nowOn = enabled ?? (wasOn || radiusMeters is not null);
        var radius = radiusMeters ?? currentRadius;

        using var update = conn.CreateCommand();
        update.CommandText = """
            UPDATE app_sessions
            SET nearby = @nearby,
                nearby_radius_m = @radius,
                anchor_lat = COALESCE(@lat, anchor_lat),
                anchor_lon = COALESCE(@lon, anchor_lon),
                anchor_accuracy_m = CASE WHEN @lat IS NULL THEN anchor_accuracy_m ELSE @accuracy END
            WHERE id = @sessionId
            """;
        update.Parameters.AddWithValue("@nearby", nowOn);
        update.Parameters.AddWithValue("@radius", (short)radius);
        update.Parameters.AddWithValue("@lat", NpgsqlTypes.NpgsqlDbType.Double, (object?)point?.Latitude ?? DBNull.Value);
        update.Parameters.AddWithValue("@lon", NpgsqlTypes.NpgsqlDbType.Double, (object?)point?.Longitude ?? DBNull.Value);
        update.Parameters.AddWithValue("@accuracy", NpgsqlTypes.NpgsqlDbType.Real,
            point?.AccuracyMeters is double accuracy ? (float)accuracy : DBNull.Value);
        update.Parameters.AddWithValue("@sessionId", sessionId);
        update.ExecuteNonQuery();

        return (NearbyUpdateOutcome.Ok, new NearbyStatus(nowOn, radius));
    }

    /// <summary>
    /// The id of the user's active session, if they have one: owned by them, not archived and still
    /// within its event length (created_at + max length, default 24 h), so an organiser who abandons a
    /// session is never locked out of creating another. Several can exist for owners from before the
    /// one-active-session rule; this returns the newest.
    /// </summary>
    public string? FindActiveOwnedSessionId(string ownerUserId, DateTimeOffset now)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, created_at, max_length_hours
            FROM app_sessions
            WHERE owner_user_id = @ownerUserId AND archived_at IS NULL AND id <> @casualSessionId
            ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("@ownerUserId", ownerUserId);
        cmd.Parameters.AddWithValue("@casualSessionId", CasualSessionId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var maxLengthHours = reader.IsDBNull(2) ? DefaultMaxLengthHours : reader.GetInt16(2);
            if (DateTimeOffset.Parse(reader.GetString(1)).AddHours(maxLengthHours) > now)
                return reader.GetString(0);
        }

        return null;
    }

    /// <summary>
    /// The owner ends a session for everyone: it is archived (nobody can join or rejoin, nearby listing
    /// stops) and every current member is marked as having left. Recording and replay stay. Ending an
    /// already-ended session succeeds. False when the session isn't the caller's, doesn't exist or is
    /// the casual session, so ids can't be probed.
    /// </summary>
    public bool EndSession(string sessionId, string userId)
    {
        if (IsCasualSession(sessionId))
            return false;

        var now = DateTimeOffset.UtcNow.ToString("O");

        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        using (var owner = conn.CreateCommand())
        {
            owner.Transaction = tx;
            owner.CommandText = "SELECT owner_user_id FROM app_sessions WHERE id = @sessionId";
            owner.Parameters.AddWithValue("@sessionId", sessionId);
            if (owner.ExecuteScalar() as string != userId)
                return false;
        }

        using (var archive = conn.CreateCommand())
        {
            archive.Transaction = tx;
            archive.CommandText = "UPDATE app_sessions SET archived_at = @now WHERE id = @sessionId AND archived_at IS NULL";
            archive.Parameters.AddWithValue("@now", now);
            archive.Parameters.AddWithValue("@sessionId", sessionId);
            archive.ExecuteNonQuery();
        }

        using (var leave = conn.CreateCommand())
        {
            leave.Transaction = tx;
            leave.CommandText = "UPDATE session_members SET left_at = @now WHERE session_id = @sessionId AND left_at IS NULL";
            leave.Parameters.AddWithValue("@now", now);
            leave.Parameters.AddWithValue("@sessionId", sessionId);
            leave.ExecuteNonQuery();
        }

        tx.Commit();
        _sessions.TryRemove(sessionId, out _);
        return true;
    }

    /// <summary>
    /// Nearby sessions the caller could join from where they are, nearest first. A session is listed
    /// when nearby is on, it isn't archived or past its event length, the caller isn't already in it
    /// or blocked with its organiser, and the caller is inside its radius (see
    /// <see cref="NearbyJoin.IsWithinRadius"/>). The exact distance stays server-side.
    /// </summary>
    public IReadOnlyList<NearbySessionMatch> FindNearbySessions(
        string userId, double latitude, double longitude, double? accuracy, DateTimeOffset now)
    {
        // A cheap latitude band so we don't measure every nearby session on earth: the largest radius
        // (500 m) plus the forgiven accuracy (2 x 50 m) is about 0.0054 degrees of latitude.
        const double LatitudeBand = 0.01;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.session_name, owner.display_name, s.nearby_radius_m,
                   s.anchor_lat, s.anchor_lon, s.anchor_accuracy_m, s.created_at, s.max_length_hours
            FROM app_sessions s
            JOIN session_members owner ON owner.session_id = s.id AND owner.user_id = s.owner_user_id
            WHERE s.nearby
              AND s.archived_at IS NULL
              AND s.anchor_lat BETWEEN @minLat AND @maxLat
              AND NOT EXISTS (
                  SELECT 1 FROM session_members me
                  WHERE me.session_id = s.id AND me.user_id = @userId AND me.left_at IS NULL
              )
              AND NOT EXISTS (
                  SELECT 1 FROM blocked_users b
                  WHERE (b.blocker_user_id = @userId AND b.blocked_user_id = s.owner_user_id)
                     OR (b.blocker_user_id = s.owner_user_id AND b.blocked_user_id = @userId)
              )
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@minLat", latitude - LatitudeBand);
        cmd.Parameters.AddWithValue("@maxLat", latitude + LatitudeBand);

        var matches = new List<NearbySessionMatch>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var maxLengthHours = reader.IsDBNull(8) ? DefaultMaxLengthHours : reader.GetInt16(8);
            if (DateTimeOffset.Parse(reader.GetString(7)).AddHours(maxLengthHours) <= now)
                continue;

            var distance = GeoMath.DistanceMeters(latitude, longitude, reader.GetDouble(4), reader.GetDouble(5));
            var radius = reader.IsDBNull(3) ? NearbyJoin.DefaultRadiusMeters : reader.GetInt16(3);
            double? anchorAccuracy = reader.IsDBNull(6) ? null : reader.GetFloat(6);
            if (!NearbyJoin.IsWithinRadius(distance, radius, accuracy, anchorAccuracy))
                continue;

            matches.Add(new NearbySessionMatch(reader.GetString(0), reader.GetString(1), reader.GetString(2), distance));
        }

        return matches.OrderBy(m => m.DistanceMeters).Take(NearbyJoin.MaxListed).ToList();
    }

    /// <summary>
    /// Joins a nearby session as a runner. The server decides: the caller must be inside the session's
    /// radius right now (same rule as the list). Returns null for every refusal - unknown, not nearby,
    /// switched off, ended, blocked, too far - so a caller can't tell which and can't probe ids.
    /// </summary>
    public SessionMembership? JoinNearbySession(
        string sessionId, string userId, string displayName,
        double latitude, double longitude, double? accuracy, DateTimeOffset now)
    {
        using var conn = Connect();
        using var lookup = conn.CreateCommand();
        lookup.CommandText = """
            SELECT invite_code, owner_user_id, nearby_radius_m, anchor_lat, anchor_lon, anchor_accuracy_m,
                   created_at, max_length_hours
            FROM app_sessions
            WHERE id = @sessionId AND nearby AND archived_at IS NULL AND anchor_lat IS NOT NULL
            """;
        lookup.Parameters.AddWithValue("@sessionId", sessionId);

        string inviteCode, ownerUserId;
        using (var reader = lookup.ExecuteReader())
        {
            if (!reader.Read())
                return null;

            var maxLengthHours = reader.IsDBNull(7) ? DefaultMaxLengthHours : reader.GetInt16(7);
            if (DateTimeOffset.Parse(reader.GetString(6)).AddHours(maxLengthHours) <= now)
                return null;

            var radius = reader.IsDBNull(2) ? NearbyJoin.DefaultRadiusMeters : reader.GetInt16(2);
            double? anchorAccuracy = reader.IsDBNull(5) ? null : reader.GetFloat(5);
            var distance = GeoMath.DistanceMeters(latitude, longitude, reader.GetDouble(3), reader.GetDouble(4));
            if (!NearbyJoin.IsWithinRadius(distance, radius, accuracy, anchorAccuracy))
                return null;

            inviteCode = reader.GetString(0);
            ownerUserId = reader.GetString(1);
        }

        if (AreBlocked(conn, ownerUserId, userId))
            return null;

        UpsertMembership(conn, sessionId, userId, "runner", displayName, now.ToString("O"));
        return GetMembership(conn, sessionId, userId, inviteCode);
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
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name, owner.display_name, s.created_at
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
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name, owner.display_name, s.created_at
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
                reader.GetString(5),
                reader.GetString(6)));

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
            SELECT s.id, s.session_name, s.invite_code, m.role, m.display_name, owner.display_name, s.created_at
            FROM session_members m
            JOIN app_sessions s ON s.id = m.session_id
            JOIN session_members owner ON owner.session_id = s.id AND owner.user_id = s.owner_user_id
            WHERE m.session_id = @sessionId AND m.user_id = @userId AND m.left_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SessionMembership(reader.GetString(0), reader.GetString(1), inviteCode ?? reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6))
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

        // A block also ends any crew link between the two (crew is mutual, so either direction).
        var (low, high) = OrderedPair(blockerUserId, blockedUserId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM crew_links WHERE user_low_id = @low AND user_high_id = @high";
            cmd.Parameters.AddWithValue("@low", low);
            cmd.Parameters.AddWithValue("@high", high);
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

    // ---- Crew ----------------------------------------------------------------------------------
    // A crew is mutual and belongs to an account. People are added only by redeeming a code the
    // other person created and sent them: single use, 24 hours, first valid redeemer wins. Methods
    // take `now` from the caller (the controller's TimeProvider) so expiry is testable.

    public const int MaxCrewSize = 50;
    public const int MaxActiveCrewCodes = 3;
    public const int CrewCodeLength = 8;
    public static readonly TimeSpan CrewCodeLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan CrewCodeRetention = TimeSpan.FromDays(7);

    // No 0/O/1/I/L, so a code read aloud or typed from a screenshot isn't ambiguous. 31 symbols
    // over 8 characters is about 40 bits, against 24 for a session invite code, because a crew
    // code names a person rather than a session.
    private const string CrewCodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string? NormalizeCrewCode(string? value)
    {
        var code = value?.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        if (code is null || code.Length != CrewCodeLength)
            return null;

        return code.All(c => CrewCodeAlphabet.Contains(c)) ? code : null;
    }

    private static string GenerateCrewCode() =>
        string.Create(CrewCodeLength, CrewCodeAlphabet, static (span, alphabet) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });

    private static string ToIso(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    // Stored in ordinal order so a pair has exactly one row whichever of the two acted.
    private static (string Low, string High) OrderedPair(string a, string b) =>
        string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);

    public string? GetNickname(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT nickname FROM users WHERE id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        return cmd.ExecuteScalar() as string;
    }

    public bool SetNickname(string userId, string? nickname)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET nickname = @nickname WHERE id = @userId";
        cmd.Parameters.Add(new NpgsqlParameter("nickname", NpgsqlDbType.Text)
        {
            Value = (object?)nickname ?? DBNull.Value
        });
        cmd.Parameters.AddWithValue("@userId", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    // Pairs where either person has blocked the other are filtered out here as well as removed by
    // BlockUser: a Production build that predates crew blocks without removing the link, and
    // Staging and Production share this database.
    public CrewOverview GetCrewOverview(string userId)
    {
        using var conn = Connect();
        return GetCrewOverview(conn, userId);
    }

    private static CrewOverview GetCrewOverview(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.id, u.nickname, u.display_name, l.created_at
            FROM crew_links l
            JOIN users u ON u.id = CASE WHEN l.user_low_id = @userId THEN l.user_high_id ELSE l.user_low_id END
            WHERE (l.user_low_id = @userId OR l.user_high_id = @userId)
              AND NOT EXISTS (
                  SELECT 1 FROM blocked_users b
                  WHERE (b.blocker_user_id = @userId AND b.blocked_user_id = u.id)
                     OR (b.blocker_user_id = u.id AND b.blocked_user_id = @userId)
              )
            ORDER BY l.created_at DESC
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        var crew = new List<CrewMember>();
        while (reader.Read())
            crew.Add(new CrewMember(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        return new CrewOverview(crew);
    }

    public bool RemoveCrewMember(string userId, string otherUserId)
    {
        var (low, high) = OrderedPair(userId, otherUserId);
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM crew_links WHERE user_low_id = @low AND user_high_id = @high";
        cmd.Parameters.AddWithValue("@low", low);
        cmd.Parameters.AddWithValue("@high", high);
        return cmd.ExecuteNonQuery() > 0;
    }

    // Creating a code while three are live retires the oldest, so "make a new code" never fails.
    public CrewCode CreateCrewCode(string userId, DateTimeOffset now)
    {
        var nowText = ToIso(now);
        var expiresAt = ToIso(now + CrewCodeLifetime);

        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        using (var cleanup = conn.CreateCommand())
        {
            cleanup.Transaction = tx;
            cleanup.CommandText = "DELETE FROM crew_codes WHERE expires_at <= @cutoff";
            cleanup.Parameters.AddWithValue("@cutoff", ToIso(now - CrewCodeRetention));
            cleanup.ExecuteNonQuery();
        }

        using (var retire = conn.CreateCommand())
        {
            retire.Transaction = tx;
            retire.CommandText = """
                DELETE FROM crew_codes
                WHERE code IN (
                    SELECT code FROM crew_codes
                    WHERE owner_user_id = @owner AND redeemed_at IS NULL AND expires_at > @now
                    ORDER BY created_at DESC
                    OFFSET @keep
                )
                """;
            retire.Parameters.AddWithValue("@owner", userId);
            retire.Parameters.AddWithValue("@now", nowText);
            retire.Parameters.AddWithValue("@keep", MaxActiveCrewCodes - 1);
            retire.ExecuteNonQuery();
        }

        // A collision on a 40-bit code is vanishingly unlikely, but ON CONFLICT DO NOTHING (rather
        // than catching the unique violation) keeps the transaction usable if it ever happens.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GenerateCrewCode();
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO crew_codes (code, owner_user_id, created_at, expires_at)
                VALUES (@code, @owner, @createdAt, @expiresAt)
                ON CONFLICT (code) DO NOTHING
                """;
            insert.Parameters.AddWithValue("@code", code);
            insert.Parameters.AddWithValue("@owner", userId);
            insert.Parameters.AddWithValue("@createdAt", nowText);
            insert.Parameters.AddWithValue("@expiresAt", expiresAt);
            if (insert.ExecuteNonQuery() == 1)
            {
                tx.Commit();
                return new CrewCode(code, expiresAt);
            }
        }

        throw new InvalidOperationException("Could not generate a unique crew code.");
    }

    public IReadOnlyList<CrewCode> GetActiveCrewCodes(string userId, DateTimeOffset now)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT code, expires_at
            FROM crew_codes
            WHERE owner_user_id = @owner AND redeemed_at IS NULL AND expires_at > @now
            ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("@owner", userId);
        cmd.Parameters.AddWithValue("@now", ToIso(now));
        using var reader = cmd.ExecuteReader();
        var codes = new List<CrewCode>();
        while (reader.Read())
            codes.Add(new CrewCode(reader.GetString(0), reader.GetString(1)));
        return codes;
    }

    public bool RevokeCrewCode(string userId, string? rawCode)
    {
        var code = NormalizeCrewCode(rawCode);
        if (code is null)
            return false;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM crew_codes
            WHERE code = @code AND owner_user_id = @owner AND redeemed_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@owner", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public (CrewCodeOutcome Outcome, CrewCodePreview? Owner) PreviewCrewCode(
        string requesterUserId, string? rawCode, DateTimeOffset now)
    {
        var code = NormalizeCrewCode(rawCode);
        if (code is null)
            return (CrewCodeOutcome.Invalid, null);

        using var conn = Connect();
        var ownerId = FindLiveCrewCodeOwner(conn, code, ToIso(now));
        if (ownerId is null || AreBlocked(conn, ownerId, requesterUserId))
            return (CrewCodeOutcome.Invalid, null);
        if (ownerId == requesterUserId)
            return (CrewCodeOutcome.OwnCode, null);

        var owner = LoadCrewIdentity(conn, ownerId);
        if (owner is null)
            return (CrewCodeOutcome.Invalid, null);

        return (CrewCodeOutcome.Ok, new CrewCodePreview(
            ownerId, owner.Value.Nickname, owner.Value.DisplayName, IsCrew(conn, ownerId, requesterUserId)));
    }

    // Redeeming makes the two people crew immediately (the owner consented by sending the code).
    // The claim is a single conditional UPDATE, so if several people hold the same code exactly one
    // wins and the rest see the same answer as for an unknown code. Redeeming a code for someone
    // who is already crew succeeds without using the code up.
    public (CrewCodeOutcome Outcome, CrewMember? Owner) RedeemCrewCode(
        string redeemerUserId, string? rawCode, DateTimeOffset now)
    {
        var code = NormalizeCrewCode(rawCode);
        if (code is null)
            return (CrewCodeOutcome.Invalid, null);

        var nowText = ToIso(now);
        using var conn = Connect();

        var ownerId = FindLiveCrewCodeOwner(conn, code, nowText);
        if (ownerId is null || AreBlocked(conn, ownerId, redeemerUserId))
            return (CrewCodeOutcome.Invalid, null);
        if (ownerId == redeemerUserId)
            return (CrewCodeOutcome.OwnCode, null);

        var owner = LoadCrewIdentity(conn, ownerId);
        if (owner is null)
            return (CrewCodeOutcome.Invalid, null);

        var (low, high) = OrderedPair(ownerId, redeemerUserId);
        if (IsCrew(conn, ownerId, redeemerUserId))
        {
            return (CrewCodeOutcome.Ok, new CrewMember(
                ownerId, owner.Value.Nickname, owner.Value.DisplayName, GetCrewSince(conn, low, high) ?? nowText));
        }

        if (CountCrew(conn, redeemerUserId) >= MaxCrewSize || CountCrew(conn, ownerId) >= MaxCrewSize)
            return (CrewCodeOutcome.CrewFull, null);

        using var tx = conn.BeginTransaction();

        using (var claim = conn.CreateCommand())
        {
            claim.Transaction = tx;
            claim.CommandText = """
                UPDATE crew_codes
                SET redeemed_by_user_id = @redeemer, redeemed_at = @now
                WHERE code = @code AND redeemed_at IS NULL AND expires_at > @now
                """;
            claim.Parameters.AddWithValue("@redeemer", redeemerUserId);
            claim.Parameters.AddWithValue("@now", nowText);
            claim.Parameters.AddWithValue("@code", code);
            if (claim.ExecuteNonQuery() != 1)
                return (CrewCodeOutcome.Invalid, null); // someone else got there first; tx rolls back on dispose
        }

        using (var link = conn.CreateCommand())
        {
            link.Transaction = tx;
            link.CommandText = """
                INSERT INTO crew_links (user_low_id, user_high_id, created_at)
                VALUES (@low, @high, @createdAt)
                ON CONFLICT DO NOTHING
                """;
            link.Parameters.AddWithValue("@low", low);
            link.Parameters.AddWithValue("@high", high);
            link.Parameters.AddWithValue("@createdAt", nowText);
            link.ExecuteNonQuery();
        }

        tx.Commit();
        return (CrewCodeOutcome.Ok, new CrewMember(ownerId, owner.Value.Nickname, owner.Value.DisplayName, nowText));
    }

    private static string? FindLiveCrewCodeOwner(NpgsqlConnection conn, string code, string nowText)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT owner_user_id
            FROM crew_codes
            WHERE code = @code AND redeemed_at IS NULL AND expires_at > @now
            """;
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@now", nowText);
        return cmd.ExecuteScalar() as string;
    }

    private static (string? Nickname, string DisplayName)? LoadCrewIdentity(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT nickname, display_name FROM users WHERE id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static bool IsCrew(NpgsqlConnection conn, string userId1, string userId2)
    {
        var (low, high) = OrderedPair(userId1, userId2);
        return GetCrewSince(conn, low, high) is not null;
    }

    private static string? GetCrewSince(NpgsqlConnection conn, string low, string high)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT created_at FROM crew_links WHERE user_low_id = @low AND user_high_id = @high";
        cmd.Parameters.AddWithValue("@low", low);
        cmd.Parameters.AddWithValue("@high", high);
        return cmd.ExecuteScalar() as string;
    }

    private static long CountCrew(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM crew_links WHERE user_low_id = @userId OR user_high_id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        return (long)(cmd.ExecuteScalar() ?? 0L);
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
                update.NextExpectedAt,
                update.IsUltraConstrained,
                update.BatteryLevel));

        // The demo session never persists positions - live viewing above is unaffected (it reads
        // the in-memory cache, not Postgres), but nothing here ever needs cleaning up.
        if (IsDemoSession(sessionId))
            return;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level)
            VALUES (@sessionId, @userId, @name, @lat, @lon, @heading, @ts, @nextExpectedAt, @isUltraConstrained, @batteryLevel)
            """;
        AddLocationParameters(cmd, sessionId, userId, update);
        cmd.ExecuteNonQuery();
    }

    // The ten values every stored position has, shared by AddPosition and the casual insert so the
    // two can't drift apart when a column is added.
    private static void AddLocationParameters(
        NpgsqlCommand cmd, string sessionId, string userId, ValidatedLocationUpdate update)
    {
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
        cmd.Parameters.Add(new NpgsqlParameter("nextExpectedAt", NpgsqlDbType.Text)
        {
            Value = update.NextExpectedAt.HasValue ? update.NextExpectedAt.Value.ToUniversalTime().ToString("O") : DBNull.Value
        });
        cmd.Parameters.AddWithValue("@isUltraConstrained", update.IsUltraConstrained);
        cmd.Parameters.Add(new NpgsqlParameter("batteryLevel", NpgsqlDbType.Smallint)
        {
            Value = update.BatteryLevel.HasValue ? update.BatteryLevel.Value : DBNull.Value
        });
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
        // purely for freshness/latency - both sources carry `nextExpectedAt`/`isUltraConstrained`
        // as of 2026-08-12 (see AddPosition), so this is no longer about which one round-trips
        // through Postgres.
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
                   runner_user_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level
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
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetInt16(8));
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
            WHERE s.id <> @casualSessionId
            GROUP BY s.id, u.username
            ORDER BY s.created_at DESC
            """;
        cmd.Parameters.AddWithValue("@casualSessionId", CasualSessionId);
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
        if (IsCasualSession(sessionId))
            return false;

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
            WHERE session_id <> @casualSessionId
            GROUP BY session_id
            ORDER BY MAX(timestamp) DESC
            """;
        cmd.Parameters.AddWithValue("@casualSessionId", CasualSessionId);
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

    // Defensive cap on rows returned from a single windowed recording fetch.
    private const int MaxRecordingRows = 20000;

    public string GetRecordingAsNdjson(string sessionId)
    {
        var runStart = GetRunStartTimestamp(sessionId);
        var updates = LoadUpdatesByTimestamp(sessionId);
        var currentRun = runStart is null ? updates : updates.Where(u => u.Timestamp >= runStart.Value).ToList();
        return string.Join("\n", currentRun.Select(u => JsonSerializer.Serialize(u, _jsonOptions)));
    }

    // Admin/ops: every stored row for a session, in full, with no max-length window or
    // MaxRecordingRows applied - for diagnosing cases where those cuts look wrong.
    public string GetAllRecordingRowsAsNdjson(string sessionId)
    {
        var updates = LoadUpdatesByTimestamp(sessionId);
        return string.Join("\n", updates.Select(u => JsonSerializer.Serialize(u, _jsonOptions)));
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
            SELECT session_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level
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
                    Timestamp: DateTimeOffset.Parse(reader.GetString(5)),
                    NextExpectedAt: reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                    IsUltraConstrained: reader.GetBoolean(7),
                    BatteryLevel: reader.IsDBNull(8) ? null : reader.GetInt16(8)
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

    // Falls back to this when a session has no max_length_hours set - pre-existing sessions, or
    // ones created by a client that doesn't send it yet (see CreateSessionRequest.MaxLengthHours).
    private const int DefaultMaxLengthHours = 24;

    // A session's recording accumulates for as long as its invite code is reused, so replay is
    // bounded to the session's own max length, counted back from its most recent ping - not by
    // scanning for a gap between pings. A long within-session silence (e.g. a backcountry dead
    // zone) no longer gets mistaken for the boundary of a separate, earlier outing.
    private DateTimeOffset? GetRunStartTimestamp(string sessionId)
    {
        using var conn = Connect();

        DateTimeOffset earliest, latest;
        using (var rangeCmd = conn.CreateCommand())
        {
            rangeCmd.CommandText = """
                SELECT MIN(timestamp), MAX(timestamp)
                FROM location_updates
                WHERE session_id = @code
                """;
            rangeCmd.Parameters.AddWithValue("@code", sessionId);
            using var reader = rangeCmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
                return null;
            earliest = DateTimeOffset.Parse(reader.GetString(0));
            latest = DateTimeOffset.Parse(reader.GetString(1));
        }

        using var lengthCmd = conn.CreateCommand();
        lengthCmd.CommandText = "SELECT max_length_hours FROM app_sessions WHERE id = @code";
        lengthCmd.Parameters.AddWithValue("@code", sessionId);
        var maxLengthHours = lengthCmd.ExecuteScalar() switch
        {
            null or DBNull => DefaultMaxLengthHours,
            var value => Convert.ToInt32(value),
        };

        var cutoff = latest - TimeSpan.FromHours(maxLengthHours);
        return cutoff > earliest ? cutoff : earliest;
    }

    private List<LocationUpdate> LoadUpdatesByTimestamp(string sessionId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level
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
                Timestamp: DateTimeOffset.Parse(reader.GetString(5)),
                NextExpectedAt: reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                IsUltraConstrained: reader.GetBoolean(7),
                BatteryLevel: reader.IsDBNull(8) ? null : reader.GetInt16(8)
            ));
        }
        return updates;
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
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level)
            VALUES (@code, NULL, @name, @lat, @lon, @heading, @ts, @nextExpectedAt, @isUltraConstrained, @batteryLevel)
            """;
        var pCode               = cmd.Parameters.Add("code",               NpgsqlDbType.Text);
        var pName               = cmd.Parameters.Add("name",               NpgsqlDbType.Text);
        var pLat                = cmd.Parameters.Add("lat",                NpgsqlDbType.Double);
        var pLon                = cmd.Parameters.Add("lon",                NpgsqlDbType.Double);
        var pHeading            = cmd.Parameters.Add("heading",            NpgsqlDbType.Double);
        var pTs                 = cmd.Parameters.Add("ts",                 NpgsqlDbType.Text);
        var pNextExpectedAt     = cmd.Parameters.Add("nextExpectedAt",     NpgsqlDbType.Text);
        var pIsUltraConstrained = cmd.Parameters.Add("isUltraConstrained", NpgsqlDbType.Boolean);
        var pBatteryLevel       = cmd.Parameters.Add("batteryLevel",       NpgsqlDbType.Smallint);

        foreach (var u in updates)
        {
            pCode.Value               = sessionId;
            pName.Value               = u.RunnerName;
            pLat.Value                = u.Latitude;
            pLon.Value                = u.Longitude;
            pHeading.Value            = u.Heading.HasValue ? (object)u.Heading.Value : DBNull.Value;
            pTs.Value                 = u.Timestamp.ToUniversalTime().ToString("O");
            pNextExpectedAt.Value     = u.NextExpectedAt.HasValue ? (object)u.NextExpectedAt.Value.ToUniversalTime().ToString("O") : DBNull.Value;
            pIsUltraConstrained.Value = u.IsUltraConstrained;
            pBatteryLevel.Value       = u.BatteryLevel.HasValue ? (object)u.BatteryLevel.Value : DBNull.Value;
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
            GROUP BY m.user_id, m.display_name, m.role, m.joined_at
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

    public bool DeleteRecording(string sessionId)
    {
        if (IsCasualSession(sessionId))
            return false;

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
        if (IsCasualSession(sourceId) || IsCasualSession(targetId))
            return 0;

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
