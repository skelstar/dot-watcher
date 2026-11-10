using Npgsql;

namespace DotWatcher.Server;

// A GPX route for one casual run. Whoever can see the runner (themselves, their crew) and anyone
// holding one of the run's watch links can fetch it; it lives only as long as the run. See
// .ai/plans/casual-tracking.md. Named sessions keep their own route (session_routes).
public partial class SessionStore
{
    /// <summary>Largest GPX body accepted for a run, in characters.</summary>
    public const int CasualRouteMaxLength = 5_000_000;

    /// <summary>
    /// Saves (or replaces) the route on the caller's open run. False when they have no open run: the
    /// route belongs to a run, so there is nowhere to put it.
    /// </summary>
    public bool SaveCasualRoute(string userId, string gpxContent, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(gpxContent))
            throw new ArgumentException("GPX content is required.", nameof(gpxContent));

        using var conn = Connect();
        var run = FindOpenCasualRun(conn, userId);
        if (run is null)
            return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO casual_run_routes (run_id, gpx_content, uploaded_at)
            VALUES (@runId, @gpx, @uploadedAt)
            ON CONFLICT (run_id) DO UPDATE SET
                gpx_content = excluded.gpx_content,
                uploaded_at = excluded.uploaded_at
            """;
        cmd.Parameters.AddWithValue("@runId", run.Id);
        cmd.Parameters.AddWithValue("@gpx", gpxContent);
        cmd.Parameters.AddWithValue("@uploadedAt", ToIso(now));
        cmd.ExecuteNonQuery();
        return true;
    }

    /// <summary>Removes the route from the caller's open run. Safe to repeat.</summary>
    public void DeleteCasualRoute(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM casual_run_routes
            WHERE run_id IN (SELECT id FROM casual_runs WHERE user_id = @userId AND ended_at IS NULL)
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>The route on an open run, or null when the run has none or has ended.</summary>
    public string? GetCasualRouteGpx(string runId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT rr.gpx_content
            FROM casual_run_routes rr
            JOIN casual_runs r ON r.id = rr.run_id AND r.ended_at IS NULL
            WHERE rr.run_id = @runId
            """;
        cmd.Parameters.AddWithValue("@runId", runId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// The route for one run if <paramref name="viewerId"/> may see that runner right now (themselves
    /// or a crew member who is visible to them), else null. Uses the same rules as the live map, so a
    /// route is never reachable by someone who couldn't see the dot.
    /// </summary>
    public string? GetCasualRouteForViewer(string viewerId, string runId, CasualAccount account, bool includeCrew, DateTimeOffset now)
    {
        var live = GetCasualLive(viewerId, account.VisibleToNonCrew, account.VisibleToCrew, includeCrew, self: null, now);
        return live.Routes?.Any(r => r.RunId == runId) == true ? GetCasualRouteGpx(runId) : null;
    }

    /// <summary>When each of these runs' routes was uploaded; runs without a route are left out.</summary>
    private static Dictionary<string, DateTimeOffset> GetCasualRouteTimes(NpgsqlConnection conn, IReadOnlyCollection<string> runIds)
    {
        var times = new Dictionary<string, DateTimeOffset>();
        if (runIds.Count == 0)
            return times;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT run_id, uploaded_at FROM casual_run_routes WHERE run_id = ANY(@runIds)";
        cmd.Parameters.AddWithValue("@runIds", runIds.ToArray());
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            times[reader.GetString(0)] = DateTimeOffset.Parse(reader.GetString(1));
        return times;
    }

    /// <summary>Whether a watch link's run has a route; false for an ended run.</summary>
    private static bool CasualRunHasRoute(NpgsqlConnection conn, string runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM casual_run_routes WHERE run_id = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>The route for a watch link's run, or null (no such link, run ended, or no route).</summary>
    public string? GetCasualWatchRoute(string token)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT rr.gpx_content
            FROM casual_watch_links w
            JOIN casual_runs r ON r.id = w.run_id AND r.ended_at IS NULL
            JOIN casual_run_routes rr ON rr.run_id = r.id
            WHERE w.token_hash = @hash
            """;
        cmd.Parameters.AddWithValue("@hash", HashWatchToken(token));
        return cmd.ExecuteScalar() as string;
    }
}
