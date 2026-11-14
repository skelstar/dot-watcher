using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace DotWatcher.Server;

// Admin replay of casual ("Go") runs (see .ai/plans/casual-tracking.md, "Replay"). A run has exact
// boundaries in casual_runs and every position it stored carries casual_run_id, so a replay is just
// that run's rows - no time-window guessing like a named session's GetRunStartTimestamp. Only the
// runner themselves is in it: no crew, and never anonymous nearby dots. Positions go after
// Casual:RetentionDays (CasualMaintenanceService), so only recent runs can be replayed.
public partial class SessionStore
{
    /// <summary>Casual runs, newest first, with how many positions each has stored.</summary>
    public IReadOnlyList<AdminCasualRun> GetCasualRunsForAdmin(int limit)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT r.id, r.user_id, u.username, u.display_name, r.started_at, r.ended_at, r.stopped_by_user,
                   COUNT(l.casual_run_id), MIN(l.timestamp), MAX(l.timestamp)
            FROM casual_runs r
            JOIN users u ON u.id = r.user_id
            LEFT JOIN location_updates l ON l.casual_run_id = r.id
            GROUP BY r.id, u.id
            ORDER BY r.started_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        var runs = new List<AdminCasualRun>();
        while (reader.Read())
        {
            runs.Add(new AdminCasualRun(
                RunId: reader.GetString(0),
                UserId: reader.GetString(1),
                Username: reader.GetString(2),
                DisplayName: reader.GetString(3),
                StartedAt: DateTimeOffset.Parse(reader.GetString(4)),
                EndedAt: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
                StoppedByUser: reader.GetBoolean(6),
                PositionCount: Convert.ToInt32(reader.GetInt64(7)),
                FirstPositionAt: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
                LastPositionAt: reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9))));
        }
        return runs;
    }

    /// <summary>
    /// The run's scrubbable range, on the phone's clock (the same <c>timestamp</c> the recording is
    /// windowed by). Null when there is no such run; null bounds when it has no positions yet.
    /// </summary>
    public RecordingMeta? GetCasualRunRecordingMeta(string runId)
    {
        using var conn = Connect();
        using (var exists = conn.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM casual_runs WHERE id = @runId";
            exists.Parameters.AddWithValue("@runId", runId);
            if (exists.ExecuteScalar() is null)
                return null;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(timestamp), MAX(timestamp) FROM location_updates WHERE casual_run_id = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new RecordingMeta(
            RunStartTimestamp: reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0)),
            LatestTimestamp: reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1)));
    }

    /// <summary>
    /// The run's positions in the recording NDJSON format, optionally windowed, capped at
    /// <see cref="MaxRecordingRows"/> like a named session's windowed fetch.
    /// </summary>
    public (string Ndjson, bool Truncated) GetCasualRunRecordingAsNdjson(
        string runId, DateTimeOffset? since, DateTimeOffset? until)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, runner_name, latitude, longitude, heading, timestamp, next_expected_at, is_ultra_constrained, battery_level
            FROM location_updates
            WHERE casual_run_id = @runId
              AND (@since IS NULL OR timestamp >= @since)
              AND (@until IS NULL OR timestamp <= @until)
            ORDER BY timestamp
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.Text)
        {
            Value = (object?)since?.ToString("O") ?? DBNull.Value
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

        return (string.Join("\n", updates.Select(u => JsonSerializer.Serialize(u, _jsonOptions))), truncated);
    }
}
