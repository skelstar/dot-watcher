namespace DotWatcher.Server;

/// <summary>
/// Keeps the never-ending casual session from growing without bound: closes abandoned runs and
/// deletes positions and finished runs older than <c>Casual:RetentionDays</c> (default 7). Also deletes
/// activity events after <c>Activity:RetentionDays</c> (30) and finished groups after
/// <c>Groups:RetentionDays</c> (7, counted from when the group was over). Staging
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
            // The casual session is shared by every account, and a build that predates the guard in
            // DeleteUserAccount could remove it; recreate it if so (a no-op otherwise).
            store.EnsureCasualSession();

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

        // Activity events (the admin Activity tab) are kept on their own schedule, not the runs'.
        try
        {
            var activityRetention = TimeSpan.FromDays(configuration.GetValue<double>("Activity:RetentionDays", 30));
            var deleted = store.PurgeActivityEvents(timeProvider.GetUtcNow() - activityRetention);
            if (deleted > 0)
                logger.LogInformation("Activity maintenance deleted {DeletedActivityEvents} events", deleted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Activity maintenance failed");
        }

        // Groups that are over (ended or past their end time) go after Groups:RetentionDays, as Go runs do.
        try
        {
            var groupRetention = TimeSpan.FromDays(configuration.GetValue<double>("Groups:RetentionDays", 7));
            var deleted = store.PurgeFinishedGroups(timeProvider.GetUtcNow(), groupRetention);
            if (deleted > 0)
                logger.LogInformation("Group maintenance deleted {DeletedGroups} finished groups", deleted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Group maintenance failed");
        }
    }
}
