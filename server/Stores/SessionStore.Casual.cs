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

    // After someone presses stop, positions copied across from a named session don't put them back
    // on the map for this long. Pressing start again ends it at once.
    public static readonly TimeSpan CasualStopSuppression = TimeSpan.FromHours(12);

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

    // user id -> until when a copy from a named session may not start a run (set by an explicit stop).
    private readonly ConcurrentDictionary<string, DateTimeOffset> _casualSuppressedUntil = new();

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

    public bool GetCasualVisibleToNonCrew(string userId) => GetCasualAccount(userId).VisibleToNonCrew;

    /// <summary>The toggle and the account's granted features, in one query.</summary>
    public CasualAccount GetCasualAccount(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT casual_visible_to_non_crew, features FROM users WHERE id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new CasualAccount(reader.GetBoolean(0), ParseFeatures(reader.GetString(1)))
            : new CasualAccount(false, []);
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
    /// client can call it on every launch. An open run the server hasn't heard from within the hard
    /// cap is closed (at the last time it was heard from) and replaced.
    /// </summary>
    public CasualStartResponse StartCasualRun(string userId, DateTimeOffset now)
    {
        _casualSuppressedUntil.TryRemove(userId, out _);
        return new(CasualSessionId, OpenOrReuseCasualRun(userId, now, honourStop: false)!.Id);
    }

    /// <summary>Ends the caller's open run, if any. They leave everyone's map at once. Idempotent.</summary>
    public void StopCasualRun(string userId, DateTimeOffset now)
    {
        _casualRuns.TryRemove(userId, out _);
        _casualSuppressedUntil[userId] = now + CasualStopSuppression;

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE casual_runs SET ended_at = @endedAt, stopped_by_user = TRUE
            WHERE user_id = @userId AND ended_at IS NULL
            """;
        cmd.Parameters.AddWithValue("@endedAt", ToIso(now));
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();

        InvalidateCasualSnapshot();
    }

    // The caller's open run, creating one if there isn't a usable one. With honourStop (a copy from a
    // named session) it returns null instead if the person pressed stop recently: a named-session
    // post must not undo that.
    private CasualRun? OpenOrReuseCasualRun(string userId, DateTimeOffset now, bool honourStop)
    {
        if (_casualRuns.TryGetValue(userId, out var cached) && now - cached.LastSeen <= CasualHardStaleCap)
            return cached;
        _casualRuns.TryRemove(userId, out _);

        if (honourStop && _casualSuppressedUntil.TryGetValue(userId, out var until) && now < until)
            return null;

        using var conn = Connect();
        var open = FindOpenCasualRun(conn, userId);
        if (open is not null)
        {
            var lastSeen = LatestCasualReceivedAt(conn, open.Id) ?? open.StartedAt;
            if (now - lastSeen <= CasualHardStaleCap)
                return _casualRuns[userId] = new CasualRun(open.Id, lastSeen, open.StartLatitude, open.StartLongitude);

            CloseCasualRun(conn, open.Id, lastSeen);
        }
        else if (honourStop && StoppedRecently(conn, userId, now))
        {
            return null;
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

    // The caller's open run without creating one - what a direct position post needs. A run the server
    // hasn't heard from within the hard cap doesn't count: the client starts again, which gives a new
    // run (and a new start point) rather than resuming an abandoned one.
    private CasualRun? GetOpenCasualRun(string userId, DateTimeOffset now)
    {
        if (_casualRuns.TryGetValue(userId, out var cached) && now - cached.LastSeen <= CasualHardStaleCap)
            return cached;
        _casualRuns.TryRemove(userId, out _);

        using var conn = Connect();
        var open = FindOpenCasualRun(conn, userId);
        if (open is null)
            return null;

        var lastSeen = LatestCasualReceivedAt(conn, open.Id) ?? open.StartedAt;
        return now - lastSeen <= CasualHardStaleCap
            ? _casualRuns[userId] = new CasualRun(open.Id, lastSeen, open.StartLatitude, open.StartLongitude)
            : null;
    }

    private bool StoppedRecently(NpgsqlConnection conn, string userId, DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(ended_at) FROM casual_runs WHERE user_id = @userId AND stopped_by_user";
        cmd.Parameters.AddWithValue("@userId", userId);
        if (cmd.ExecuteScalar() is not string stoppedAt)
            return false;

        var until = DateTimeOffset.Parse(stoppedAt) + CasualStopSuppression;
        if (now >= until)
            return false;

        _casualSuppressedUntil[userId] = until;
        return true;
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

    // The server's clock at the run's most recent live position.
    private static DateTimeOffset? LatestCasualReceivedAt(NpgsqlConnection conn, string runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(received_at) FROM location_updates WHERE casual_run_id = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
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
    /// (<paramref name="startRunIfNone"/> false) and gets NoRun without one, so a delayed post after
    /// the user pressed stop can't bring them back. A post copied across from a named session starts
    /// a run if there isn't one, unless the person pressed stop recently. A position older than the
    /// stale window is a queued one flushed late: a direct post keeps it (for replay) without moving
    /// the runner's dot, and a copy from a named session ignores it.
    /// </summary>
    public CasualPostResult AddCasualPosition(
        ValidatedLocationUpdate update, string userId, bool inSession, bool startRunIfNone, DateTimeOffset now)
    {
        var historical = now - update.Timestamp > CasualStaleAfter;
        if (historical && startRunIfNone)
            return new CasualPostResult(CasualPostOutcome.Historical);

        // Twice: the first attempt can find the cached run was closed behind this process's back.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var run = startRunIfNone
                ? OpenOrReuseCasualRun(userId, now, honourStop: true)
                : GetOpenCasualRun(userId, now);
            if (run is null)
                return new CasualPostResult(CasualPostOutcome.NoRun);

            if (!TryInsertCasualPosition(userId, run, update, inSession, historical, now))
            {
                // Stopped on the other server, or closed as abandoned. Forget it and look again.
                _casualRuns.TryRemove(userId, out _);
                continue;
            }

            if (historical)
                return new CasualPostResult(CasualPostOutcome.Historical);

            run.LastSeen = now;
            return new CasualPostResult(
                CasualPostOutcome.Live,
                new CasualLiveEntry(
                    userId,
                    new RunnerPosition(
                        update.RunnerName, update.Latitude, update.Longitude, update.Heading, update.Timestamp,
                        update.NextExpectedAt, update.IsUltraConstrained, update.BatteryLevel),
                    inSession,
                    VisibleToNonCrew: true,
                    run.StartLatitude,
                    run.StartLongitude,
                    now));
        }

        return new CasualPostResult(CasualPostOutcome.NoRun);
    }

    // Inserts only if the run is still open in the database (not just in this process's cache), and
    // sets the run's start point in the same transaction so a reader never sees the position without
    // it. False when the run has been closed.
    private bool TryInsertCasualPosition(
        string userId, CasualRun run, ValidatedLocationUpdate update, bool inSession, bool historical, DateTimeOffset now)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        var setsStart = !historical && run.StartLatitude is null;
        if (setsStart)
        {
            using var start = conn.CreateCommand();
            start.Transaction = tx;
            start.CommandText = """
                UPDATE casual_runs SET start_lat = @lat, start_lon = @lon
                WHERE id = @id AND start_lat IS NULL AND ended_at IS NULL
                """;
            start.Parameters.AddWithValue("@lat", update.Latitude);
            start.Parameters.AddWithValue("@lon", update.Longitude);
            start.Parameters.AddWithValue("@id", run.Id);
            start.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO location_updates (session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level, in_session, casual_run_id, received_at)
            SELECT @sessionId, @userId, @name, @lat, @lon, @heading, @ts, @nextExpectedAt, @isUltraConstrained, @batteryLevel, @inSession, @runId, @receivedAt
            WHERE EXISTS (SELECT 1 FROM casual_runs WHERE id = @runId AND ended_at IS NULL)
            """;
        AddLocationParameters(cmd, CasualSessionId, userId, update);
        cmd.Parameters.AddWithValue("@inSession", inSession);
        cmd.Parameters.AddWithValue("@runId", run.Id);
        cmd.Parameters.Add(new NpgsqlParameter("receivedAt", NpgsqlDbType.Text)
        {
            Value = historical ? DBNull.Value : ToIso(now)
        });
        if (cmd.ExecuteNonQuery() == 0)
        {
            tx.Rollback();
            return false;
        }

        tx.Commit();
        if (setsStart)
        {
            run.StartLatitude = update.Latitude;
            run.StartLongitude = update.Longitude;
        }
        return true;
    }

    // ---- Reading -------------------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="viewerId"/> can see right now. <paramref name="self"/> is the entry just
    /// built from the viewer's own post, so they see themselves without waiting for the snapshot.
    /// Nearby dots are only returned to someone who is out themselves, and are centred on where the
    /// server last placed them, never on a position the caller supplies: otherwise any opted-in
    /// account could scan the map for strangers. <paramref name="includeCrew"/> is false for accounts
    /// without the crew feature, who then see everyone else as a non-crew dot.
    /// </summary>
    public CasualLiveResponse GetCasualLive(
        string viewerId,
        bool viewerVisibleToNonCrew,
        bool includeCrew,
        CasualLiveEntry? self,
        DateTimeOffset now)
    {
        var entries = GetCasualSnapshot(now).ToList();
        if (self is not null)
        {
            entries.RemoveAll(e => e.UserId == viewerId);
            entries.Add(self);
        }

        var me = entries.FirstOrDefault(e => e.UserId == viewerId && CasualVisibility.IsLive(e, now));
        (double Latitude, double Longitude)? viewerPosition =
            me is null ? null : (me.Position.Latitude, me.Position.Longitude);

        using var conn = Connect();
        var crewLabels = new Dictionary<string, string>();
        if (includeCrew)
            foreach (var member in GetCrewOverview(conn, viewerId).Crew)
                crewLabels[member.UserId] = string.IsNullOrWhiteSpace(member.Nickname) ? member.DisplayName : member.Nickname;

        return CasualVisibility.ForViewer(
            viewerId,
            viewerVisibleToNonCrew,
            viewerPosition,
            crewLabels,
            GetBlockedPartyIds(conn, viewerId),
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

    // Everyone whose open run has a live position received inside the hard cap, taking each person's
    // latest. Only positions belonging to the open run count, so a restart never shows the last run's
    // final position. Read from Postgres rather than an in-memory cache because Staging and
    // Production share the database but not memory.
    private IReadOnlyList<CasualLiveEntry> LoadCasualSnapshot(DateTimeOffset now)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT ON (l.runner_user_id)
                   l.runner_user_id, l.runner_name, l.latitude, l.longitude, l.heading, l.timestamp,
                   l.next_expected_at, l.is_ultra_constrained, l.battery_level, l.in_session, l.received_at,
                   u.casual_visible_to_non_crew, r.start_lat, r.start_lon
            FROM location_updates l
            JOIN casual_runs r ON r.id = l.casual_run_id AND r.ended_at IS NULL
            JOIN users u ON u.id = l.runner_user_id
            WHERE l.session_id = @sessionId AND l.received_at >= @since
            ORDER BY l.runner_user_id, l.received_at DESC
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
                reader.GetBoolean(11),
                reader.IsDBNull(12) ? null : reader.GetDouble(12),
                reader.IsDBNull(13) ? null : reader.GetDouble(13),
                DateTimeOffset.Parse(reader.GetString(10))));
        }
        return entries;
    }

    // Everyone who has blocked the user or been blocked by them. Blocking is symmetric here.
    public IReadOnlySet<string> GetBlockedPartyIds(string userId)
    {
        using var conn = Connect();
        return GetBlockedPartyIds(conn, userId);
    }

    private static IReadOnlySet<string> GetBlockedPartyIds(NpgsqlConnection conn, string userId)
    {
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
        _casualSuppressedUntil.TryRemove(userId, out _);
        InvalidateCasualSnapshot();
    }

    // ---- Retention -----------------------------------------------------------------------------

    /// <summary>
    /// Closes runs the server hasn't heard from within the hard cap, at the last time it did, then
    /// deletes casual positions and finished runs older than <paramref name="retention"/>. Safe to
    /// run from both servers at once.
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
                    (SELECT MAX(l.received_at) FROM location_updates l WHERE l.casual_run_id = r.id),
                    r.started_at)
                WHERE r.ended_at IS NULL
                  AND r.started_at < @staleBefore
                  AND NOT EXISTS (
                      SELECT 1 FROM location_updates l
                      WHERE l.casual_run_id = r.id AND l.received_at >= @staleBefore)
                """;
            cmd.Parameters.AddWithValue("@staleBefore", ToIso(now - CasualHardStaleCap));
            closed = cmd.ExecuteNonQuery();
        }

        var cutoff = ToIso(now - retention);

        int deletedPositions;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM location_updates
                WHERE session_id = @sessionId
                  AND COALESCE(received_at, timestamp) < @cutoff
                """;
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
