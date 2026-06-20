namespace DotWatcher.Server;

public class LogBufferProvider(LogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogBufferLogger(buffer, categoryName);
    public void Dispose() { }
}
