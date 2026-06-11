using System.Collections.Concurrent;

namespace DotChaser.Server;

public class SessionStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RunnerPosition>>> _sessions = new();

    public void AddPosition(LocationUpdate update)
    {
        var session = _sessions.GetOrAdd(update.SessionCode, _ => new());
        var history = session.GetOrAdd(update.RunnerName, _ => []);
        lock (history)
            history.Add(new RunnerPosition(update.RunnerName, update.Latitude, update.Longitude, update.Heading, update.Timestamp));
    }

    public IReadOnlyList<RunnerPosition> GetLatestPositions(string sessionCode)
    {
        if (!_sessions.TryGetValue(sessionCode, out var session))
            return [];

        var result = new List<RunnerPosition>(session.Count);
        foreach (var (_, history) in session)
        {
            lock (history)
            {
                if (history.Count > 0)
                    result.Add(history[^1]);
            }
        }
        return result;
    }

    public void ClearSession(string sessionCode) => _sessions.TryRemove(sessionCode, out _);
}
