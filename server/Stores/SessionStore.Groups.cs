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
        createdAt.AddHours(maxLengthHours ?? DefaultMaxLengthHours) > now;

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
}
