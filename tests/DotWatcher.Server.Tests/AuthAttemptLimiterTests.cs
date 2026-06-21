using Microsoft.Extensions.Configuration;
using Xunit;

namespace DotWatcher.Server.Tests;

public class AuthAttemptLimiterTests
{
    [Fact]
    public void IsLocked_AfterAttemptWindowExpires_PrunesIdleFailureWindow()
    {
        var timeProvider = new ManualTimeProvider();
        var limiter = new AuthAttemptLimiter(
            Config(
                ("AuthMaxFailedAttempts", "3"),
                ("AuthAttemptWindowMinutes", "1")),
            timeProvider);

        limiter.RecordFailure("127.0.0.1:alice");
        Assert.Equal(1, limiter.TrackedAttemptCount);

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        Assert.False(limiter.IsLocked("127.0.0.1:alice", out _));
        Assert.Equal(0, limiter.TrackedAttemptCount);
    }

    [Fact]
    public void RecordFailure_WhenTrackedAttemptLimitIsExceeded_PrunesOldestIdleWindows()
    {
        var limiter = new AuthAttemptLimiter(
            Config(
                ("AuthMaxFailedAttempts", "3"),
                ("AuthAttemptWindowMinutes", "60"),
                ("AuthMaxTrackedAttempts", "2")),
            new ManualTimeProvider());

        limiter.RecordFailure("127.0.0.1:alice");
        limiter.RecordFailure("127.0.0.1:bob");
        limiter.RecordFailure("127.0.0.1:charlie");

        Assert.Equal(2, limiter.TrackedAttemptCount);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
    {
        var entries = values.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries)
            .Build();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
