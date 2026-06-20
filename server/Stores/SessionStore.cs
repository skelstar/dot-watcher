using System.Collections.Concurrent;
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
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Connect()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void AddPosition(LocationUpdate update)
    {
        var code = update.SessionCode.ToUpperInvariant();

        var session = _sessions.GetOrAdd(code, _ => new());
        var history = session.GetOrAdd(update.RunnerName, _ => []);
        lock (history)
            history.Add(new RunnerPosition(update.RunnerName, update.Latitude, update.Longitude, update.Heading, update.Timestamp));

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
        var code = sessionCode.ToUpperInvariant();
        var updates = ndjsonContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<LocationUpdate>(line, _jsonOptions))
            .OfType<LocationUpdate>()
            .ToList();

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
}
