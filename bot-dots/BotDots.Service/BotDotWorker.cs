using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotDots.Service;

/// <summary>
/// The fleet loop. Every <see cref="BotDotsOptions.PostIntervalSeconds"/> it moves each bot-dot on and posts
/// its position; every <see cref="BotDotsOptions.SyncIntervalSeconds"/> it asks the server where people are
/// (coarse cells only) and spawns or retires bot-dots to match. All state is in memory and disposable: after a
/// restart the old dots go stale on the server within minutes and new ones start.
/// </summary>
public sealed class BotDotWorker(
    BotServerClient server,
    Wanderer wanderer,
    IOptions<BotDotsOptions> options,
    TimeProvider time,
    ILogger<BotDotWorker> logger) : BackgroundService
{
    private const double ExtendWhenRemainingBelowM = 300;

    private readonly BotDotsOptions _options = options.Value;
    private readonly List<BotRunner> _fleet = [];
    private readonly Random _rng = new();
    private DateTimeOffset _lastSync = DateTimeOffset.MinValue;
    private bool _disabledLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PostIntervalSeconds), time);
        var last = time.GetUtcNow();
        try
        {
            do
            {
                var now = time.GetUtcNow();
                var elapsed = (now - last).TotalSeconds;
                last = now;
                try
                {
                    await TickAsync(now, elapsed, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Bot-dots tick failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            await StopAllAsync();
        }
    }

    private async Task TickAsync(DateTimeOffset now, double elapsedSeconds, CancellationToken cancellationToken)
    {
        if (now - _lastSync >= TimeSpan.FromSeconds(_options.SyncIntervalSeconds))
        {
            _lastSync = now;
            await SyncAsync(now, cancellationToken);
        }

        var toPost = new List<BotRunner>();
        foreach (var bot in _fleet.ToList())
        {
            bot.Advance(elapsedSeconds);

            if (now >= bot.TripEndsAt)
                bot.Retiring = true;

            if (!bot.Retiring && bot.RemainingMeters < ExtendWhenRemainingBelowM
                && !await wanderer.ExtendAsync(bot, _rng, cancellationToken))
            {
                logger.LogInformation("{Bot} is boxed in and will stop", bot.Username);
                bot.Retiring = true;
            }

            if (bot.Retiring && bot.RemainingMeters < 5)
            {
                await StopAsync(bot, cancellationToken);
                _fleet.Remove(bot);
                continue;
            }

            toPost.Add(bot);
        }

        await Parallel.ForEachAsync(
            toPost,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (bot, token) => await PostAsync(bot, now, token));
    }

    private async Task SyncAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cells = await server.GetCellsAsync(cancellationToken);
        if (cells is null)
        {
            // Switched off on the server: let everyone finish their current hop and stop.
            if (!_disabledLogged)
                logger.LogInformation("Bot-dots are switched off on the server (Bots:Enabled); idling");
            _disabledLogged = true;
            foreach (var bot in _fleet)
                bot.Retiring = true;
            return;
        }

        _disabledLogged = false;
        var plan = Spawner.Plan(cells, _fleet, _options);
        foreach (var bot in plan.Retire)
            bot.Retiring = true;

        foreach (var request in plan.Spawn)
            for (var i = 0; i < request.Count; i++)
                await SpawnAsync(request.Cell, now, cancellationToken);

        logger.LogInformation(
            "Bot-dots: {Out} out ({Retiring} finishing) across {Cells} cells with people",
            _fleet.Count, _fleet.Count(b => b.Retiring), cells.Count(c => c.RealCount > 0));
    }

    private async Task SpawnAsync(CellInfo cell, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var index = Enumerable.Range(1, _options.MaxBots).FirstOrDefault(i => _fleet.All(b => b.AccountIndex != i));
        if (index == 0)
            return;

        // It only knows the cell, never a person: start somewhere around the cell's middle.
        var start = Geo.Destination(
            cell.Centre,
            _rng.NextDouble() * 360,
            _options.SpawnMinM + _rng.NextDouble() * (_options.SpawnMaxM - _options.SpawnMinM));
        var tripMinutes = _options.TripMinMinutes + _rng.NextDouble() * (_options.TripMaxMinutes - _options.TripMinMinutes);
        var bot = new BotRunner(
            index, Persona.Pick(_rng), cell.Cell, cell.Centre, start, now.AddMinutes(tripMinutes), new Random(_rng.Next()));

        // Route first (a start in the sea simply fails here), then sign in.
        if (!await wanderer.ExtendAsync(bot, _rng, cancellationToken))
        {
            logger.LogDebug("No route from a spawn point in {Cell}", cell.Cell);
            return;
        }

        if (!await SignInAndStartAsync(bot, cancellationToken))
        {
            logger.LogWarning("{Bot} could not sign in or start", bot.Username);
            return;
        }

        _fleet.Add(bot);
        logger.LogInformation("{Bot} ({Persona}) out in {Cell} for {Minutes:0} min", bot.Username, bot.Persona.Name, cell.Cell, tripMinutes);
    }

    private async Task<bool> SignInAndStartAsync(BotRunner bot, CancellationToken cancellationToken)
    {
        bot.Token = await server.SignInAsync(bot.Username, cancellationToken);
        if (bot.Token is null)
            return false;

        bot.SessionId = await server.StartAsync(bot.Token, cancellationToken);
        return bot.SessionId is not null;
    }

    private async Task PostAsync(BotRunner bot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (bot.Token is null || bot.SessionId is null)
            {
                if (!await SignInAndStartAsync(bot, cancellationToken))
                    return;
            }

            var outcome = await server.PostLocationAsync(
                bot.Token!, bot.SessionId!, bot.NoisyPosition(), bot.Heading,
                now, now.AddSeconds(_options.PostIntervalSeconds), cancellationToken);

            switch (outcome)
            {
                case PostOutcome.Ok:
                    return;
                case PostOutcome.NoRun:
                    bot.SessionId = await server.StartAsync(bot.Token!, cancellationToken);
                    break;
                case PostOutcome.SignedOut:
                    bot.Token = null;
                    break;
                default:
                    return; // a network blip: the next tick tries again
            }
        }
    }

    private async Task StopAsync(BotRunner bot, CancellationToken cancellationToken)
    {
        if (bot.Token is null)
            return;

        try
        {
            await server.StopAsync(bot.Token, cancellationToken);
            logger.LogInformation("{Bot} finished", bot.Username);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "{Bot} stop failed; the server will time it out", bot.Username);
        }
    }

    // On shutdown, end every run so the dots leave the map now instead of after the server's stale timeout.
    private async Task StopAllAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var bot in _fleet)
        {
            try
            {
                await StopAsync(bot, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _fleet.Clear();
    }
}
