using System.Collections.Concurrent;

namespace DotWatcher.Server;

public class LogBuffer
{
    private readonly ConcurrentQueue<string> _lines = new();
    private const int MaxLines = 300;

    public void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > MaxLines)
            _lines.TryDequeue(out _);
    }

    public string[] GetAll() => _lines.ToArray();

    public void Clear() { while (_lines.TryDequeue(out _)) { } }
}
