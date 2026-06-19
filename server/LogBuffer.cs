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

public class LogBufferProvider(LogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogBufferLogger(buffer, categoryName);
    public void Dispose() { }
}

file class LogBufferLogger(LogBuffer buffer, string categoryName) : ILogger
{
    private static readonly HashSet<string> _skipPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
        "Microsoft.Hosting",
    ];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Information &&
        !_skipPrefixes.Any(p => categoryName.StartsWith(p, StringComparison.Ordinal));

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var level = logLevel switch
        {
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "INFO "
        };
        var msg = formatter(state, exception);
        if (exception is not null) msg += $" | {exception.Message}";
        buffer.Add($"[{DateTime.Now:HH:mm:ss}] {level}  {msg}");
    }
}
