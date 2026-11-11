namespace BotDots.Service;

/// <summary>One roughly 1 km cell (geohash length 6) from the server's <c>GET /admin/casual/cells</c>.</summary>
public sealed record CellInfo(string Cell, double Latitude, double Longitude, int RealCount, int BotDotCount)
{
    public LatLon Centre => new(Latitude, Longitude);
}

public sealed record SpawnRequest(CellInfo Cell, int Count);

public sealed record SpawnPlan(IReadOnlyList<SpawnRequest> Spawn, IReadOnlyList<BotRunner> Retire);

/// <summary>
/// Decides where bot-dots are needed. Bot-dots fill gaps, they don't replace people: a cell with real runners
/// gets topped up to <see cref="BotDotsOptions.TargetPerCell"/> dots, and the bot-dots thin out (finish their
/// trip, don't start another) as real runners arrive or leave. Pure, so it is unit-tested.
/// </summary>
public static class Spawner
{
    public static int Want(int realCount, BotDotsOptions options) =>
        realCount <= 0 ? 0 : Math.Clamp(options.TargetPerCell - realCount, 0, options.MaxPerCell);

    public static SpawnPlan Plan(IReadOnlyList<CellInfo> cells, IReadOnlyCollection<BotRunner> fleet, BotDotsOptions options)
    {
        var real = cells.ToDictionary(c => c.Cell, c => c.RealCount);
        var active = fleet.Where(b => !b.Retiring).GroupBy(b => b.CellId).ToDictionary(g => g.Key, g => g.ToList());

        var retire = new List<BotRunner>();
        foreach (var (cell, bots) in active)
        {
            var want = Want(real.GetValueOrDefault(cell), options);
            // Retire the ones with the most trip still to go; the others are about to finish anyway.
            retire.AddRange(bots.OrderByDescending(b => b.TripEndsAt).Take(Math.Max(0, bots.Count - want)));
        }

        var capacity = options.MaxBots - fleet.Count;
        var spawn = new List<SpawnRequest>();
        foreach (var cell in cells.Where(c => c.RealCount > 0).OrderBy(c => c.Cell, StringComparer.Ordinal))
        {
            var have = active.TryGetValue(cell.Cell, out var list) ? list.Count : 0;
            var count = Math.Min(Want(cell.RealCount, options) - have, Math.Min(options.MaxSpawnPerCellPerSync, capacity));
            if (count <= 0)
                continue;

            spawn.Add(new SpawnRequest(cell, count));
            capacity -= count;
        }

        return new SpawnPlan(spawn, retire);
    }
}
