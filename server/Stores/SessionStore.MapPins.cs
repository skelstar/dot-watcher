using Npgsql;

namespace DotWatcher.Server;

// Message pins (see .ai/plans/session-map-pins.md and MapPinModels.cs). Named-session pins live in
// session_pins: any runner in the session can drop one, everyone in it sees them, and the author or the
// session's owner can delete one. Casual pins live in casual_run_pins, on the runner's own open run:
// the same people who can see that run's route see them (the runner, their visible crew, anyone with a
// watch link), and they are deleted when the run stops.
public partial class SessionStore
{
    // ---- Named sessions ------------------------------------------------------------------------

    /// <summary>
    /// The session's pins for a member, oldest first, leaving out anyone the viewer has blocked or been
    /// blocked by. <paramref name="viewerId"/> null is the tokenless web viewer: no blocks, nothing is theirs.
    /// </summary>
    public IReadOnlyList<MapMessagePin> GetSessionPins(string sessionId, string? viewerId)
    {
        using var conn = Connect();
        IReadOnlySet<string> blocked = viewerId is null ? new HashSet<string>() : GetBlockedPartyIds(conn, viewerId);
        var isOwner = viewerId is not null && GetSessionOwnerId(conn, sessionId) == viewerId;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, display_name, lat, lon, message, created_at
            FROM session_pins
            WHERE session_id = @sessionId
            ORDER BY created_at, id
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = cmd.ExecuteReader();
        var pins = new List<MapMessagePin>();
        while (reader.Read())
        {
            var authorId = reader.GetString(1);
            if (blocked.Contains(authorId))
                continue;
            var isMine = viewerId is not null && authorId == viewerId;
            pins.Add(new MapMessagePin(
                reader.GetString(0),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6)),
                IsMine: isMine,
                CanDelete: isMine || isOwner));
        }
        return pins;
    }

    /// <summary>Drops a pin in a session the caller is a runner in (the controller checks that), as their session name.</summary>
    public MapPinCreateResult CreateSessionPin(string sessionId, string userId, double latitude, double longitude, string message, DateTimeOffset now)
    {
        using var conn = Connect();
        var membership = GetMembership(conn, sessionId, userId);
        if (membership is null)
            return new MapPinCreateResult(MapPinCreateOutcome.NoRun);

        using var tx = conn.BeginTransaction();
        // Serialise creates per session so two at once can't both slip under the cap.
        using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT pg_advisory_xact_lock(hashtext('session_pins:' || @sessionId))";
            lockCmd.Parameters.AddWithValue("@sessionId", sessionId);
            lockCmd.ExecuteNonQuery();
        }

        if (CountRows(conn, tx, "SELECT COUNT(1) FROM session_pins WHERE session_id = @id", sessionId) >= MapPinRules.MaxPins)
            return new MapPinCreateResult(MapPinCreateOutcome.Full);

        var pin = new MapMessagePin(Guid.NewGuid().ToString(), membership.DisplayName, latitude, longitude, message, now, IsMine: true, CanDelete: true);
        using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO session_pins (id, session_id, user_id, display_name, lat, lon, message, created_at)
                VALUES (@id, @sessionId, @userId, @displayName, @lat, @lon, @message, @createdAt)
                """;
            insert.Parameters.AddWithValue("@id", pin.Id);
            insert.Parameters.AddWithValue("@sessionId", sessionId);
            insert.Parameters.AddWithValue("@userId", userId);
            insert.Parameters.AddWithValue("@displayName", pin.Author);
            insert.Parameters.AddWithValue("@lat", latitude);
            insert.Parameters.AddWithValue("@lon", longitude);
            insert.Parameters.AddWithValue("@message", message);
            insert.Parameters.AddWithValue("@createdAt", ToIso(now));
            insert.ExecuteNonQuery();
        }
        tx.Commit();
        return new MapPinCreateResult(MapPinCreateOutcome.Created, pin);
    }

    /// <summary>
    /// Deletes a session pin if the caller wrote it or owns the session. Null when there is no such pin
    /// in the session; false when it exists but the caller may not delete it.
    /// </summary>
    public bool? DeleteSessionPin(string sessionId, string pinId, string userId)
    {
        using var conn = Connect();
        using var lookup = conn.CreateCommand();
        lookup.CommandText = "SELECT user_id FROM session_pins WHERE id = @id AND session_id = @sessionId";
        lookup.Parameters.AddWithValue("@id", pinId);
        lookup.Parameters.AddWithValue("@sessionId", sessionId);
        if (lookup.ExecuteScalar() is not string authorId)
            return null;
        if (authorId != userId && GetSessionOwnerId(conn, sessionId) != userId)
            return false;

        using var delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM session_pins WHERE id = @id";
        delete.Parameters.AddWithValue("@id", pinId);
        delete.ExecuteNonQuery();
        return true;
    }

    private static string? GetSessionOwnerId(NpgsqlConnection conn, string sessionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT owner_user_id FROM app_sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);
        return cmd.ExecuteScalar() as string;
    }

    private static long CountRows(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    // ---- Casual runs ---------------------------------------------------------------------------

    /// <summary>Drops a pin on the caller's open run. <see cref="MapPinCreateOutcome.NoRun"/> when they aren't out.</summary>
    public MapPinCreateResult CreateCasualPin(string userId, double latitude, double longitude, string message, DateTimeOffset now)
    {
        using var conn = Connect();
        var run = FindOpenCasualRun(conn, userId);
        if (run is null)
            return new MapPinCreateResult(MapPinCreateOutcome.NoRun);

        using var tx = conn.BeginTransaction();
        using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT pg_advisory_xact_lock(hashtext('casual_run_pins:' || @runId))";
            lockCmd.Parameters.AddWithValue("@runId", run.Id);
            lockCmd.ExecuteNonQuery();
        }

        if (CountRows(conn, tx, "SELECT COUNT(1) FROM casual_run_pins WHERE run_id = @id", run.Id) >= MapPinRules.MaxPins)
            return new MapPinCreateResult(MapPinCreateOutcome.Full);

        var id = Guid.NewGuid().ToString();
        using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO casual_run_pins (id, run_id, lat, lon, message, created_at)
                VALUES (@id, @runId, @lat, @lon, @message, @createdAt)
                """;
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@runId", run.Id);
            insert.Parameters.AddWithValue("@lat", latitude);
            insert.Parameters.AddWithValue("@lon", longitude);
            insert.Parameters.AddWithValue("@message", message);
            insert.Parameters.AddWithValue("@createdAt", ToIso(now));
            insert.ExecuteNonQuery();
        }
        tx.Commit();

        var author = GetUserDisplayName(conn, userId) ?? "";
        return new MapPinCreateResult(
            MapPinCreateOutcome.Created,
            new MapMessagePin(id, author, latitude, longitude, message, now, IsMine: true, CanDelete: true));
    }

    /// <summary>Deletes one of the pins on the caller's open run. False when there is no such pin there.</summary>
    public bool DeleteCasualPin(string userId, string pinId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM casual_run_pins
            WHERE id = @id
              AND run_id IN (SELECT id FROM casual_runs WHERE user_id = @userId AND ended_at IS NULL)
            """;
        cmd.Parameters.AddWithValue("@id", pinId);
        cmd.Parameters.AddWithValue("@userId", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Every pin on the caller's open run, for when it stops: pins go with the run.</summary>
    private static void DeleteOpenCasualRunPins(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM casual_run_pins
            WHERE run_id IN (SELECT id FROM casual_runs WHERE user_id = @userId AND ended_at IS NULL)
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Pins on runs that have ended (abandoned runs closed by the purge). Returns how many went.</summary>
    private static int DeleteEndedCasualRunPins(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM casual_run_pins
            WHERE run_id IN (SELECT id FROM casual_runs WHERE ended_at IS NOT NULL)
            """;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>The pins on these open runs, grouped by run, oldest first.</summary>
    private static Dictionary<string, List<StoredCasualPin>> GetCasualRunPins(NpgsqlConnection conn, IReadOnlyCollection<string> runIds)
    {
        var pins = new Dictionary<string, List<StoredCasualPin>>();
        if (runIds.Count == 0)
            return pins;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.run_id, p.lat, p.lon, p.message, p.created_at
            FROM casual_run_pins p
            JOIN casual_runs r ON r.id = p.run_id AND r.ended_at IS NULL
            WHERE p.run_id = ANY(@runIds)
            ORDER BY p.created_at, p.id
            """;
        cmd.Parameters.AddWithValue("@runIds", runIds.ToArray());
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var pin = new StoredCasualPin(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5)));
            if (!pins.TryGetValue(pin.RunId, out var list))
                pins[pin.RunId] = list = [];
            list.Add(pin);
        }
        return pins;
    }

    /// <summary>The pins on a watch link's run, labelled with the runner's initials; empty once it has ended.</summary>
    private static IReadOnlyList<MapMessagePin> GetCasualWatchPins(NpgsqlConnection conn, string runId, string label) =>
        GetCasualRunPins(conn, [runId]).TryGetValue(runId, out var pins)
            ? pins.Select(p => new MapMessagePin(p.Id, label, p.Latitude, p.Longitude, p.Message, p.CreatedAt)).ToList()
            : [];

    private static string? GetUserDisplayName(NpgsqlConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT display_name FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() as string;
    }
}
