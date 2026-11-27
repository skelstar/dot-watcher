using Npgsql;

namespace DotWatcher.Server;

// Groups, step 1 (see .ai/plans/go-and-groups.md): people who share an active named session see each
// other by name in casual, the way crew do, without anyone switching what they track against. Named
// sessions stand in for groups for now, so every one uses the "Everyone" rule. Nothing here is stored:
// the labels are worked out on each live read and simply stop once the session ends.
public partial class SessionStore
{
    /// <summary>
    /// Whether a named session still counts as a group: created less than its event length ago
    /// (max_length_hours, default 24 h). Archiving is checked separately, in the query.
    /// </summary>
    public static bool IsGroupActive(DateTimeOffset createdAt, int? maxLengthHours, DateTimeOffset now) =>
        GroupEndsAt(createdAt, maxLengthHours) > now;

    /// <summary>When a named session (a group) ends: created plus its event length, default 24 h. In UTC.</summary>
    public static DateTimeOffset GroupEndsAt(DateTimeOffset createdAt, int? maxLengthHours) =>
        createdAt.AddHours(maxLengthHours ?? DefaultMaxLengthHours).ToUniversalTime();

    /// <summary>
    /// <see cref="GroupEndsAt"/> as ISO-8601, read from a row's created_at and max_length_hours columns.
    /// </summary>
    private static string EndsAtIso(NpgsqlDataReader reader, int createdAtColumn, int maxLengthColumn)
    {
        int? maxLengthHours = reader.IsDBNull(maxLengthColumn) ? null : reader.GetInt16(maxLengthColumn);
        return GroupEndsAt(DateTimeOffset.Parse(reader.GetString(createdAtColumn)), maxLengthHours).ToString("O");
    }

    /// <summary>
    /// The people <paramref name="viewerId"/> shares an active group with, keyed by user id and labelled
    /// with the display name they use in that group. Both the viewer and the other person must still be
    /// members (not left), the session must not be archived, and the casual and demo sessions never
    /// count. If two people share more than one group, the newest group's label wins. Blocks are
    /// applied later, by <see cref="CasualVisibility.ForViewer"/>, as they are for crew.
    /// </summary>
    private Dictionary<string, string> GetGroupMemberLabels(NpgsqlConnection conn, string viewerId, DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT other.user_id, other.display_name, s.created_at, s.max_length_hours
            FROM session_members me
            JOIN app_sessions s ON s.id = me.session_id
            JOIN session_members other
              ON other.session_id = s.id AND other.user_id <> me.user_id AND other.left_at IS NULL
            WHERE me.user_id = @viewerId
              AND me.left_at IS NULL
              AND s.archived_at IS NULL
              AND s.id <> @casualSessionId
              AND s.id <> @demoSessionId
            ORDER BY s.created_at DESC
            """;
        cmd.Parameters.AddWithValue("@viewerId", viewerId);
        cmd.Parameters.AddWithValue("@casualSessionId", CasualSessionId);
        cmd.Parameters.AddWithValue("@demoSessionId", DemoSessionId ?? "");

        var labels = new Dictionary<string, string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int? maxLengthHours = reader.IsDBNull(3) ? null : reader.GetInt16(3);
            if (!IsGroupActive(DateTimeOffset.Parse(reader.GetString(2)), maxLengthHours, now))
                continue;
            labels.TryAdd(reader.GetString(0), reader.GetString(1));
        }

        return labels;
    }
    /// <summary>
    /// Deletes groups that have been over for longer than <paramref name="retention"/>, with their members:
    /// over means ended (archived) or past their end time, whichever came first. Sessions with recorded
    /// positions or a route are kept, so an older session-tracked run keeps its replay; groups never have
    /// either (members track on Go). The casual and demo sessions are never touched. Returns how many went.
    /// Safe to run from both servers at once.
    /// </summary>
    public int PurgeFinishedGroups(DateTimeOffset now, TimeSpan retention)
    {
        var cutoff = now - retention;
        var finished = new List<string>();
        using (var conn = Connect())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.id, s.created_at, s.max_length_hours, s.archived_at
                FROM app_sessions s
                WHERE s.id <> @casualSessionId AND s.id <> @demoSessionId
                  AND NOT EXISTS (SELECT 1 FROM location_updates l WHERE l.session_id = s.id)
                  AND NOT EXISTS (SELECT 1 FROM session_routes r WHERE r.session_id = s.id)
                """;
            cmd.Parameters.AddWithValue("@casualSessionId", CasualSessionId);
            cmd.Parameters.AddWithValue("@demoSessionId", DemoSessionId ?? "");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int? maxLengthHours = reader.IsDBNull(2) ? null : reader.GetInt16(2);
                var overAt = GroupEndsAt(DateTimeOffset.Parse(reader.GetString(1)), maxLengthHours);
                if (!reader.IsDBNull(3))
                {
                    var archivedAt = DateTimeOffset.Parse(reader.GetString(3));
                    if (archivedAt < overAt)
                        overAt = archivedAt;
                }
                if (overAt < cutoff)
                    finished.Add(reader.GetString(0));
            }
        }

        var deleted = 0;
        foreach (var sessionId in finished)
        {
            if (DeleteSession(sessionId))
                deleted++;
        }

        return deleted;
    }
}
