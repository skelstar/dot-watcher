using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace DotWatcher.Server;

/// <summary>A dot with activity in a time range, for the admin Activity tab's picker.</summary>
public sealed record ActivityDot(string UserId, string DisplayName, string Username, int EventCount);

/// <summary>One recorded activity event, as the admin Activity tab reads it.</summary>
public sealed record ActivityEventView(
    long Id,
    DateTimeOffset At,
    string UserId,
    string Kind,
    string? OtherUserId,
    string? Client,
    string? AppVersion,
    string? RunId,
    string? SessionId,
    JsonElement? Detail,
    // Marked as testing by an admin (the Activity tab), so it can be left out.
    bool IsTest = false);

// Activity events: what each dot did, written as it happens, for the admin Activity tab's flow. See
// .ai/plans/activity-flow.md. Additive and cascading like the rest of the schema.
public partial class SessionStore
{
    private static void InitializeActivity(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS activity_events (
                id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                at            TIMESTAMPTZ NOT NULL,
                user_id       TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                kind          TEXT NOT NULL,
                other_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
                device_id     TEXT,
                client        TEXT,
                app_version   TEXT,
                -- No foreign key: the history outlives the casual clean-up, which deletes runs.
                run_id        TEXT,
                session_id    TEXT,
                dedupe_key    TEXT,
                detail        JSONB
            );
            CREATE INDEX IF NOT EXISTS idx_activity_user_at ON activity_events(user_id, at);
            CREATE INDEX IF NOT EXISTS idx_activity_at ON activity_events(at);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_activity_dedupe ON activity_events(dedupe_key) WHERE dedupe_key IS NOT NULL;
            -- Testing on the live server: an admin marks a run or a stretch as test afterwards, and the
            -- Activity tab leaves it out unless asked. Nothing is deleted, so it can be unmarked.
            ALTER TABLE activity_events ADD COLUMN IF NOT EXISTS is_test BOOLEAN NOT NULL DEFAULT FALSE;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes activity events. One whose account has gone since (deleted between the request and the
    /// write) is skipped, and so is one whose dedupe key is already in the table.
    /// </summary>
    public void InsertActivityEvents(IReadOnlyList<ActivityEvent> events)
    {
        if (events.Count == 0)
            return;

        using var conn = Connect();
        foreach (var activity in events)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO activity_events
                    (at, user_id, kind, other_user_id, device_id, client, app_version, run_id, session_id, dedupe_key, detail)
                SELECT @at, @userId, @kind,
                       (SELECT id FROM users WHERE id = @otherUserId),
                       @deviceId, @client, @appVersion, @runId, @sessionId, @dedupeKey, CAST(@detail AS JSONB)
                WHERE EXISTS (SELECT 1 FROM users WHERE id = @userId)
                ON CONFLICT (dedupe_key) WHERE dedupe_key IS NOT NULL DO NOTHING
                """;
            cmd.Parameters.AddWithValue("@at", activity.At.UtcDateTime);
            cmd.Parameters.AddWithValue("@userId", activity.UserId);
            cmd.Parameters.AddWithValue("@kind", activity.Kind);
            AddNullableText(cmd, "otherUserId", activity.OtherUserId);
            AddNullableText(cmd, "deviceId", activity.DeviceId);
            AddNullableText(cmd, "client", activity.Client);
            AddNullableText(cmd, "appVersion", activity.AppVersion);
            AddNullableText(cmd, "runId", activity.RunId);
            AddNullableText(cmd, "sessionId", activity.SessionId);
            AddNullableText(cmd, "dedupeKey", activity.DedupeKey);
            AddNullableText(cmd, "detail", activity.DetailJson);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Everyone with activity in [from, to), busiest first. Test events count only with <paramref name="includeTest"/>.</summary>
    public IReadOnlyList<ActivityDot> GetActivityDots(DateTimeOffset from, DateTimeOffset to, bool includeTest = false)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.user_id, u.display_name, u.username, COUNT(*)::INT
            FROM activity_events a
            JOIN users u ON u.id = a.user_id
            WHERE a.at >= @from AND a.at < @to AND (@includeTest OR NOT a.is_test)
            GROUP BY a.user_id, u.display_name, u.username
            ORDER BY COUNT(*) DESC, u.display_name
            """;
        cmd.Parameters.AddWithValue("@from", from.UtcDateTime);
        cmd.Parameters.AddWithValue("@to", to.UtcDateTime);
        cmd.Parameters.AddWithValue("@includeTest", includeTest);

        var dots = new List<ActivityDot>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            dots.Add(new ActivityDot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return dots;
    }

    /// <summary>The given dots' events in [from, to), oldest first. Test events only with <paramref name="includeTest"/>.</summary>
    public IReadOnlyList<ActivityEventView> GetActivityEvents(
        DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<string> userIds, bool includeTest = false)
    {
        if (userIds.Count == 0)
            return [];

        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, at, user_id, kind, other_user_id, client, app_version, run_id, session_id, detail::TEXT, is_test
            FROM activity_events
            WHERE at >= @from AND at < @to AND user_id = ANY(@userIds) AND (@includeTest OR NOT is_test)
            ORDER BY at, id
            """;
        cmd.Parameters.AddWithValue("@from", from.UtcDateTime);
        cmd.Parameters.AddWithValue("@to", to.UtcDateTime);
        cmd.Parameters.AddWithValue("@userIds", userIds.ToArray());
        cmd.Parameters.AddWithValue("@includeTest", includeTest);

        var events = new List<ActivityEventView>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            var detail = Text(9);
            events.Add(new ActivityEventView(
                reader.GetInt64(0),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                reader.GetString(2),
                reader.GetString(3),
                Text(4),
                Text(5),
                Text(6),
                Text(7),
                Text(8),
                detail is null ? null : JsonDocument.Parse(detail).RootElement.Clone(),
                reader.GetBoolean(10)));
        }
        return events;
    }

    /// <summary>
    /// Marks one dot's events as test (or not): a whole Go run when <paramref name="runId"/> is given,
    /// which takes in everything they did from its first event to its last (app opens and the like
    /// carry no run), else everything in [from, to). Returns how many events it changed.
    /// </summary>
    public int MarkActivityTest(string userId, bool isTest, string? runId, DateTimeOffset? from, DateTimeOffset? to)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        if (runId is not null)
        {
            cmd.CommandText = """
                WITH span AS (
                    SELECT MIN(at) AS first_at, MAX(at) AS last_at
                    FROM activity_events WHERE user_id = @userId AND run_id = @runId)
                UPDATE activity_events a SET is_test = @isTest
                FROM span
                WHERE a.user_id = @userId AND a.is_test <> @isTest
                  AND (a.run_id = @runId OR a.at BETWEEN span.first_at AND span.last_at)
                """;
            cmd.Parameters.AddWithValue("@runId", runId);
        }
        else
        {
            cmd.CommandText = """
                UPDATE activity_events SET is_test = @isTest
                WHERE user_id = @userId AND is_test <> @isTest AND at >= @from AND at < @to
                """;
            cmd.Parameters.AddWithValue("@from", from!.Value.UtcDateTime);
            cmd.Parameters.AddWithValue("@to", to!.Value.UtcDateTime);
        }
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@isTest", isTest);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Adds fields to the detail of the event with this dedupe key (a watch viewing's length).</summary>
    public void MergeActivityDetail(string dedupeKey, string detailJson)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE activity_events
            SET detail = COALESCE(detail, '{}'::JSONB) || CAST(@detail AS JSONB)
            WHERE dedupe_key = @dedupeKey
            """;
        cmd.Parameters.AddWithValue("@dedupeKey", dedupeKey);
        AddNullableText(cmd, "detail", detailJson);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Deletes activity events older than <paramref name="before"/>.</summary>
    public int PurgeActivityEvents(DateTimeOffset before)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM activity_events WHERE at < @before";
        cmd.Parameters.AddWithValue("@before", before.UtcDateTime);
        return cmd.ExecuteNonQuery();
    }

    private static void AddNullableText(NpgsqlCommand cmd, string name, string? value) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
}
