using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotWatcher.Server;

public class SessionStore(int positionHistoryCount, string recordingsPath)
{
    private static readonly JsonSerializerOptions _ndjsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RunnerPosition>>> _sessions = new();
    private readonly ConcurrentDictionary<string, object> _fileLocks = new();

    public void AddPosition(LocationUpdate update)
    {
        var code = update.SessionCode.ToUpperInvariant();
        var session = _sessions.GetOrAdd(code, _ => new());
        var history = session.GetOrAdd(update.RunnerName, _ => []);
        lock (history)
            history.Add(new RunnerPosition(update.RunnerName, update.Latitude, update.Longitude, update.Heading, update.Timestamp));

        var fileLock = _fileLocks.GetOrAdd(code, _ => new object());
        lock (fileLock)
        {
            Directory.CreateDirectory(recordingsPath);
            var path = Path.Combine(recordingsPath, $"{code}.ndjson");
            File.AppendAllText(path, JsonSerializer.Serialize(update with { SessionCode = code }, _ndjsonOptions) + "\n");
        }
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
        if (!Directory.Exists(recordingsPath))
            return [];
        return Directory.GetFiles(recordingsPath, "*.ndjson")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderDescending();
    }

    public string? GetRecordingPath(string sessionCode)
    {
        var path = Path.Combine(recordingsPath, $"{sessionCode.ToUpperInvariant()}.ndjson");
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    public void ClearSession(string sessionCode) => _sessions.TryRemove(sessionCode.ToUpperInvariant(), out _);
}
