using System.Collections.Concurrent;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;

namespace DotWatcher.Server;

// Casual tracking (see .ai/plans/casual-tracking.md): one permanent session that anyone with the
// "casual" feature can be out in, without creating or joining anything. Who sees whom is decided
// per viewer by CasualVisibility; this file holds the session, the runs, and the position storage.
//
// Authorization deliberately does not use session_members. Casual has no membership rows, so
//  - a build that predates casual (Staging and Production share one database) never lists it under
//    GET /me/sessions, and
//  - every session-scoped endpoint, which checks membership, refuses it with no extra code.
public partial class SessionStore
{
    // Fixed (and a valid GUID, which location validation requires) so Staging and Production, which
    // share a database and each call EnsureCasualSession on startup, agree on one session.
    public const string CasualSessionId = "ca5ca100-0000-4000-8000-000000000001";

    // A dot disappears this long after its last position, extended to nextExpectedAt + the grace
    // (so a slow satellite cadence doesn't drop it), never beyond the hard cap.
    public static readonly TimeSpan CasualStaleAfter = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CasualNextExpectedGrace = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan CasualHardStaleCap = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long one read of everyone currently out is reused. Every runner polls every ~15s, so
    /// without this each poll would query Postgres for the whole session. Zero disables the reuse.
    /// </summary>
    public TimeSpan CasualSnapshotLifetime { get; set; } = TimeSpan.FromSeconds(5);

    private sealed class CasualRun(string id, DateTimeOffset lastSeen, double? startLatitude, double? startLongitude)
    {
        public string Id { get; } = id;
        public DateTimeOffset LastSeen { get; set; } = lastSeen;
        public double? StartLatitude { get; set; } = startLatitude;
        public double? StartLongitude { get; set; } = startLongitude;
    }

    private sealed record OpenCasualRun(string Id, DateTimeOffset StartedAt, double? StartLatitude, double? StartLongitude);

    // user id -> their open run, so a position post doesn't have to look it up. Per process like
    // `_sessions`; a miss (or an entry older than the hard cap) falls back to Postgres.
    private readonly ConcurrentDictionary<string, CasualRun> _casualRuns = new();

    private readonly object _casualSnapshotLock = new();
    private IReadOnlyList<CasualLiveEntry> _casualSnapshot = [];
    private DateTimeOffset _casualSnapshotAt = DateTimeOffset.MinValue;

    public bool IsCasualSession(string sessionId) => sessionId == CasualSessionId;

    // Idempotent - safe to call on every startup, from both servers. The session's invite code is
    // 32 hex characters: session invite codes are 4-8 characters (NormalizeSessionName), so no
    // invite-code endpoint, old or new, can ever look it up. That matters because those endpoints
    // are unauthenticated and would otherwise expose every runner's position.
    public void EnsureCasualSession()
    {
        const string casualUsername = "dw-casual";
        CreateUser(new UserAccount(
            Guid.NewGuid().ToString(),
            casualUsername,
            "Casual",
            PasswordHasher.Hash(Guid.NewGuid().ToString())));
        var ownerUserId = GetUserByUsername(casualUsername)!.Id;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_sessions (id, session_name, invite_code, owner_user_id, created_at)
            VALUES (@id, @sessionName, @inviteCode, @ownerUserId, @createdAt)
            ON CONFLICT (id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("@id", CasualSessionId);
        cmd.Parameters.AddWithValue("@sessionName", "Casual");
        cmd.Parameters.AddWithValue("@inviteCode", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        cmd.Parameters.AddWithValue("@ownerUserId", ownerUserId);
        cmd.Parameters.AddWithValue("@createdAt", ToIso(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    // ---- Settings ------------------------------------------------------------------------------

    public bool GetCasualVisibleToNonCrew(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT casual_visible_to_non_crew FROM users WHERE id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        return cmd.ExecuteScalar() is true;
    }

    public bool SetCasualVisibleToNonCrew(string userId, bool visible)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET casual_visible_to_non_crew = @visible WHERE id = @userId";
        cmd.Parameters.AddWithValue("@visible", visible);
        cmd.Parameters.AddWithValue("@userId", userId);
        var changed = cmd.ExecuteNonQuery() > 0;
        InvalidateCasualSnapshot();
        return changed;
    }

    // ---- Runs ----------------------------------------------------------------------------------

    /// <summary>
    /// Starts the caller's casual run, or continues the one they already have. Idempotent, so a
    /// client can call it on every launch. An open run whose last position is older than the hard
    /// cap is closed (at that last position) and replaced.
    /// </summary>
    public CasualStartResponse StartCasualRun(string userId, DateTimeOffset now) =>
        new(CasualSessionId, OpenOrReuseCasualRun(userId, now).Id);

    /// <summary>Ends the caller's open run, if any. They leave everyone's map at once. Idempotent.</summary>
    public void StopCasualRun(string userId, DateTimeOffset now)
    {
        _casualRuns.TryRemove(userId, out _);

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE casual_runs SET ended_at = @endedAt WHERE user_id = @userId AND ended_at IS NULL";
        cmd.Parameters.AddWithValue("@endedAt", ToIso(now));
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();

        InvalidateCasualSnapshot();
    }

    private CasualRun OpenOrReuseCasualRun(string userId, DateTimeOffset now)
    {
        if (_casualRuns.TryGetValue(userId, out var cached) && now - cached.LastSeen <= CasualHardStaleCap)
        {
            cached.LastSeen = now;
            return cached;
        }

        using var conn = Connect();
        var open = FindOpenCasualRun(conn, userId);
        if (open is not null)
        {
            var lastSeen = LatestCasualPositionAt(conn, userId, open.StartedAt) ?? open.StartedAt;
            if (now - lastSeen <= CasualHardStaleCap)
                return _casualRuns[userId] = new CasualRun(open.Id, now, open.StartLatitude, open.StartLongitude);

            CloseCasualRun(conn, open.Id, lastSeen);
        }

        var id = Guid.NewGuid().ToString();
        try
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO casual_runs (id, user_id, started_at) VALUES (@id, @userId, @startedAt)";
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@userId", userId);
            insert.Parameters.AddWithValue("@startedAt", ToIso(now));
            insert.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Two requests started a run for the same account at once; the other one won.
            var winner = FindOpenCasualRun(conn, userId)!;
            return _casualRuns[userId] = new CasualRun(winner.Id, now, winner.StartLatitude, winner.StartLongitude);
        }

        return _casualRuns[userId] = new CasualRun(id, now, null, null);
    }

    // The caller's open run without creating one - what a position post needs.
    private CasualRun? GetOpenCasualRun(string userId, DateTimeOffset now)
    {
        if (_casualRuns.TryGetValue(userId, out var cached) && now - cached.LastSeen <= CasualHardStaleCap)
            return cached;

        _casualRuns.TryRemove(userId, out _);
        using var conn = Connect();
        var open = FindOpenCasualRun(conn, userId);
        return open is null
            ? null
            : _casualRuns[userId] = new CasualRun(open.Id, now, open.StartLatitude, open.StartLongitude);
    }

    private static OpenCasualRun? FindOpenCasualRun(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, started_at, start_lat, start_lon
            FROM casual_runs
            WHERE user_id = @userId AND ended_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new OpenCasualRun(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3))
            : null;
    }

    private static DateTimeOffset? LatestCasualPositionAt(NpgsqlConnection conn, string userId, DateTimeOffset since)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT MAX(timestamp) FROM location_updates
            WHERE session_id = @sessionId AND runner_user_id = @userId AND timestamp >= @since
            """;
        cmd.Parameters.AddWithValue("@sessionId", CasualSessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@since", ToIso(since));
        return cmd.ExecuteScalar() is string latest ? DateTimeOffset.Parse(latest) : null;
    }

    private static void CloseCasualRun(NpgsqlConnection conn, string runId, DateTimeOffset endedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE casual_runs SET ended_at = @endedAt WHERE id = @id AND ended_at IS NULL";
        cmd.Parameters.AddWithValue("@endedAt", ToIso(endedAt));
        cmd.Parameters.AddWithValue("@id", runId);
        cmd.ExecuteNonQuery();
    }

    // ---- Positions -----------------------------------------------------------------------------

    /// <summary>
    /// Stores a position in the casual session. A direct casual post needs an open run
    /// (<paramref name="startRunIfNone"/> false) and gets null back without one, so a delayed post
    /// after the user pressed stop can't bring them back. A post copied across from a named session
    /// starts a run if there isn't one. Returns the caller's own entry for the response.
    /// </summary>
    public CasualLiveEntry? AddCasualPosition(
        ValidatedLocationUpdate update, string userId, bool inSession, bool startRunIfNone, DateTimeOffset now)
    {
        var run = startRunIfNone ? OpenOrReuseCasualRun(userId, now) : GetOpenCasualRun(userId, now);
        if (run is null)
            return null;

        run.LastSeen = now;

        using var conn = Connect();

        if (run.StartLatitude is null)
        {
            run.StartLatitude = update.Latitude;
            run.StartLongitude = update.Longitude;
            using var start = conn.CreateCommand();
            start.CommandText = "UPDATE casual_runs SET start_lat = @lat, start_lon = @lon WHERE id = @id AND start_lat IS NULL";
            start.Parameters.AddWithValue("@lat", update.Latitude);
            start.Parameters.AddWithValue("@lon", update.Longitude);
            start.Parameters.AddWithValue("@id", run.Id);
            start.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level, in_session)
            VALUES (@sessionId, @userId, @name, @lat, @lon, @heading, @ts, @nextExpectedAt, @isUltraConstrained, @batteryLevel, @inSession)
            """;
        cmd.Parameters.AddWithValue("@sessionId", CasualSessionId);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@name", update.RunnerName);
        cmd.Parameters.AddWithValue("@lat", update.Latitude);
        cmd.Parameters.AddWithValue("@lon", update.Longitude);
        cmd.Parameters.Add(new NpgsqlParameter("heading", NpgsqlDbType.Double)
        {
            Value = update.Heading.HasValue ? update.Heading.Value : DBNull.Value
        });
        cmd.Parameters.AddWithValue("@ts", ToIso(update.Timestamp));
        cmd.Parameters.Add(new NpgsqlParameter("nextExpectedAt", NpgsqlDbType.Text)
        {
            Value = update.NextExpectedAt.HasValue ? ToIso(update.NextExpectedAt.Value) : DBNull.Value
        });
        cmd.Parameters.AddWithValue("@isUltraConstrained", update.IsUltraConstrained);
        cmd.Parameters.Add(new NpgsqlParameter("batteryLevel", NpgsqlDbType.Smallint)
        {
            Value = update.BatteryLevel.HasValue ? update.BatteryLevel.Value : DBNull.Value
        });
        cmd.Parameters.AddWithValue("@inSession", inSession);
        cmd.ExecuteNonQuery();

        return new CasualLiveEntry(
            userId,
            new RunnerPosition(
                update.RunnerName, update.Latitude, update.Longitude, update.Heading, update.Timestamp,
                update.NextExpectedAt, update.IsUltraConstrained, update.BatteryLevel),
            inSession,
            VisibleToNonCrew: true,
            run.StartLatitude,
            run.StartLongitude);
    }

    // ---- Reading -------------------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="viewerId"/> can see right now. <paramref name="self"/> is the entry just
    /// built from the viewer's own post, so they see themselves without waiting for the snapshot.
    /// <paramref name="includeCrew"/> is false for accounts without the crew feature, who then see
    /// everyone else as a non-crew dot.
    /// </summary>
    public CasualLiveResponse GetCasualLive(
        string viewerId,
        bool includeCrew,
        (double Latitude, double Longitude)? viewerPosition,
        CasualLiveEntry? self,
        DateTimeOffset now)
    {
        var entries = GetCasualSnapshot(now).ToList();
        if (self is not null)
        {
            entries.RemoveAll(e => e.UserId == viewerId);
            entries.Add(self);
        }

        var crewLabels = new Dictionary<string, string>();
        if (includeCrew)
            foreach (var member in GetCrewOverview(viewerId).Crew)
                crewLabels[member.UserId] = string.IsNullOrWhiteSpace(member.Nickname) ? member.DisplayName : member.Nickname;

        return CasualVisibility.ForViewer(
            viewerId,
            GetCasualVisibleToNonCrew(viewerId),
            viewerPosition,
            crewLabels,
            GetBlockedPartyIds(viewerId),
            entries,
            now);
    }

    private IReadOnlyList<CasualLiveEntry> GetCasualSnapshot(DateTimeOffset now)
    {
        lock (_casualSnapshotLock)
        {
            var age = now - _casualSnapshotAt;
            if (age >= TimeSpan.Zero && age < CasualSnapshotLifetime)
                return _casualSnapshot;

            _casualSnapshot = LoadCasualSnapshot(now);
            _casualSnapshotAt = now;
            return _casualSnapshot;
        }
    }

    private void InvalidateCasualSnapshot()
    {
        lock (_casualSnapshotLock)
            _casualSnapshotAt = DateTimeOffset.MinValue;
    }

    // Everyone with an open run and a position inside the hard cap. Read from Postgres rather than
    // an in-memory cache because Staging and Production share the database but not memory.
    private IReadOnlyList<CasualLiveEntry> LoadCasualSnapshot(DateTimeOffset now)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT l.runner_user_id, l.runner_name, l.latitude, l.longitude, l.heading, l.timestamp,
                   l.next_expected_at, l.is_ultra_constrained, l.battery_level, l.in_session,
                   u.casual_visible_to_non_crew, r.start_lat, r.start_lon
            FROM (
                SELECT DISTINCT ON (runner_user_id)
                       runner_user_id, runner_name, latitude, longitude, heading, timestamp,
                       next_expected_at, is_ultra_constrained, battery_level, in_session
                FROM location_updates
                WHERE session_id = @sessionId AND runner_user_id IS NOT NULL AND timestamp >= @since
                ORDER BY runner_user_id, timestamp DESC
            ) l
            JOIN users u ON u.id = l.runner_user_id
            JOIN casual_runs r ON r.user_id = l.runner_user_id AND r.ended_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@sessionId", CasualSessionId);
        cmd.Parameters.AddWithValue("@since", ToIso(now - CasualHardStaleCap));
        using var reader = cmd.ExecuteReader();
        var entries = new List<CasualLiveEntry>();
        while (reader.Read())
        {
            entries.Add(new CasualLiveEntry(
                reader.GetString(0),
                new RunnerPosition(
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    DateTimeOffset.Parse(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetInt16(8)),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12)));
        }
        return entries;
    }

    // Everyone who has blocked the user or been blocked by them. Blocking is symmetric here.
    public IReadOnlySet<string> GetBlockedPartyIds(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT blocked_user_id FROM blocked_users WHERE blocker_user_id = @userId
            UNION
            SELECT blocker_user_id FROM blocked_users WHERE blocked_user_id = @userId
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        var ids = new HashSet<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    // Account deletion removes the user's rows in the database (casual_runs cascades); this clears
    // what this process holds.
    private void ForgetCasualUser(string userId)
    {
        _casualRuns.TryRemove(userId, out _);
        InvalidateCasualSnapshot();
    }

    // ---- Retention -----------------------------------------------------------------------------

    /// <summary>
    /// Closes runs that were abandoned (no position inside the hard cap) at their last position,
    /// then deletes casual positions and finished runs older than <paramref name="retention"/>.
    /// Safe to run from both servers at once.
    /// </summary>
    public (int ClosedRuns, int DeletedPositions, int DeletedRuns) PurgeCasualData(DateTimeOffset now, TimeSpan retention)
    {
        using var conn = Connect();

        int closed;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE casual_runs r
                SET ended_at = COALESCE(
                    (SELECT MAX(l.timestamp) FROM location_updates l
                     WHERE l.session_id = @sessionId AND l.runner_user_id = r.user_id AND l.timestamp >= r.started_at),
                    r.started_at)
                WHERE r.ended_at IS NULL
                  AND r.started_at < @staleBefore
                  AND NOT EXISTS (
                      SELECT 1 FROM location_updates l
                      WHERE l.session_id = @sessionId AND l.runner_user_id = r.user_id AND l.timestamp >= @staleBefore)
                """;
            cmd.Parameters.AddWithValue("@sessionId", CasualSessionId);
            cmd.Parameters.AddWithValue("@staleBefore", ToIso(now - CasualHardStaleCap));
            closed = cmd.ExecuteNonQuery();
        }

        var cutoff = ToIso(now - retention);

        int deletedPositions;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM location_updates WHERE session_id = @sessionId AND timestamp < @cutoff";
            cmd.Parameters.AddWithValue("@sessionId", CasualSessionId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            deletedPositions = cmd.ExecuteNonQuery();
        }

        int deletedRuns;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM casual_runs WHERE ended_at IS NOT NULL AND ended_at < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            deletedRuns = cmd.ExecuteNonQuery();
        }

        if (closed > 0)
            InvalidateCasualSnapshot();

        return (closed, deletedPositions, deletedRuns);
    }
}
