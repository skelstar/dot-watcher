namespace DotWatcher.Server;

/// <summary>
/// Keeps the never-ending casual session from growing without bound: closes abandoned runs and
/// deletes positions and finished runs older than <c>Casual:RetentionDays</c> (default 7). Staging
/// and Production both run it against the shared database; it is idempotent, so that is harmless.
/// </summary>
public sealed class CasualMaintenanceService(
    SessionStore store,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<CasualMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                RunOnce();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void RunOnce()
    {
        try
        {
            var retention = TimeSpan.FromDays(configuration.GetValue<double>("Casual:RetentionDays", 7));
            var (closedRuns, deletedPositions, deletedRuns) = store.PurgeCasualData(timeProvider.GetUtcNow(), retention);
            if (closedRuns + deletedPositions + deletedRuns > 0)
                logger.LogInformation(
                    "Casual maintenance closed {ClosedRuns} runs and deleted {DeletedPositions} positions and {DeletedRuns} runs",
                    closedRuns, deletedPositions, deletedRuns);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Casual maintenance failed");
        }
    }
}
