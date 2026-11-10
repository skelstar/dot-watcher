namespace BotDots.Service;

/// <summary>
/// Plans where a bot-dot goes next: a target roughly ahead of it, routed with <see cref="IRouter"/>, kept inside
/// the allowed distance from the cell's middle and away from ground it has already covered. Proven in the
/// bot-dots/spike scripts (all walks stayed in band, about 6% revisited).
/// </summary>
public sealed class Wanderer(IRouter router, BotDotsOptions options)
{
    private const int Attempts = 8;
    private const double AcceptableOverlap = 0.4;

    /// <summary>Adds one hop to the bot-dot's path. False when nothing suitable was found (it is boxed in).</summary>
    public async Task<bool> ExtendAsync(BotRunner bot, Random rng, CancellationToken cancellationToken)
    {
        RouteResult? best = null;
        var bestOverlap = double.MaxValue;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            // Roughly ahead: within 100 degrees of the way it is facing, so it keeps going instead of dithering.
            // After half the attempts, head back toward the middle instead (within 30 degrees), as a runner at
            // the edge of their patch turns round. From anywhere in the band, such a target is always inside it.
            var bearing = attempt < Attempts / 2
                ? Geo.NormalizeDegrees(bot.Heading + (rng.NextDouble() * 200 - 100))
                : Geo.NormalizeDegrees(Geo.BearingDegrees(bot.Position, bot.Centre) + (rng.NextDouble() * 60 - 30));
            var distance = options.HopMinM + rng.NextDouble() * (options.HopMaxM - options.HopMinM);
            var target = Geo.Destination(bot.Position, bearing, distance);
            if (Geo.DistanceMeters(bot.Centre, target) > options.MaxDistanceFromCentreM)
                continue;

            var route = await router.RouteAsync(bot.Position, target, bot.Heading, cancellationToken);
            if (route is null || route.Shape.Count < 2)
                continue;

            // A long way round (a river, a motorway) isn't a hop, it is a detour.
            if (route.LengthMeters > 2.5 * Geo.DistanceMeters(bot.Position, target) + 200)
                continue;

            if (route.Shape.Any(p => Geo.DistanceMeters(bot.Centre, p) > options.MaxDistanceFromCentreM * 1.15))
                continue;

            var overlap = bot.OverlapFraction(route.Shape);
            if (overlap < bestOverlap)
            {
                best = route;
                bestOverlap = overlap;
            }

            if (overlap <= AcceptableOverlap)
                break;
        }

        if (best is null)
            return false;

        bot.AppendRoute(best.Shape);
        return true;
    }
}
