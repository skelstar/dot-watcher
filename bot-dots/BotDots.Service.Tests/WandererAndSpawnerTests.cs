using Xunit;
using static BotDots.Service.Tests.MovementTests;

namespace BotDots.Service.Tests;

public class WandererTests
{
    // Routes in a straight line, as if every street ran exactly where it was asked to.
    private sealed class StraightRouter : IRouter
    {
        public int Calls { get; private set; }

        public double? LastHeading { get; private set; }

        public Task<RouteResult?> RouteAsync(LatLon from, LatLon to, double? startHeading, CancellationToken cancellationToken)
        {
            Calls++;
            LastHeading = startHeading;
            var shape = Enumerable.Range(0, 11)
                .Select(i => new LatLon(from.Lat + (to.Lat - from.Lat) * i / 10, from.Lon + (to.Lon - from.Lon) * i / 10))
                .ToList();
            return Task.FromResult<RouteResult?>(new RouteResult(shape, Geo.DistanceMeters(from, to)));
        }
    }

    private sealed class NoRouter : IRouter
    {
        public Task<RouteResult?> RouteAsync(LatLon from, LatLon to, double? startHeading, CancellationToken cancellationToken) =>
            Task.FromResult<RouteResult?>(null);
    }

    [Fact]
    public async Task Extend_AddsAHop_InsideTheBand_StartingTheWayItFaces()
    {
        var router = new StraightRouter();
        var options = new BotDotsOptions();
        var bot = Bot();

        Assert.True(await new Wanderer(router, options).ExtendAsync(bot, new Random(1), CancellationToken.None));

        Assert.InRange(bot.RemainingMeters, options.HopMinM - 1, options.HopMaxM + 1);
        Assert.Equal(bot.Heading, router.LastHeading);
    }

    [Fact]
    public async Task Wandering_ForAnHour_NeverLeavesTheBand()
    {
        var options = new BotDotsOptions();
        var wanderer = new Wanderer(new StraightRouter(), options);
        var bot = Bot(start: Geo.Destination(Centre, 45, 1500));
        var rng = new Random(5);

        for (var tick = 0; tick < 240; tick++) // an hour at 15 s
        {
            if (bot.RemainingMeters < 300)
                Assert.True(await wanderer.ExtendAsync(bot, rng, CancellationToken.None));
            bot.Advance(15);
            Assert.True(Geo.DistanceMeters(Centre, bot.Position) <= options.MaxDistanceFromCentreM * 1.15);
        }
    }

    [Fact]
    public async Task Extend_WithNoRoutes_ReportsBoxedIn()
    {
        var bot = Bot();

        Assert.False(await new Wanderer(new NoRouter(), new BotDotsOptions()).ExtendAsync(bot, new Random(1), CancellationToken.None));
        Assert.Equal(0, bot.RemainingMeters);
    }

    [Fact]
    public async Task Extend_DoesNotAskTheRouter_ForTargetsOutsideTheBand()
    {
        var router = new StraightRouter();
        // Already at the edge, with a band smaller than any hop: every target is out of bounds.
        var options = new BotDotsOptions { MaxDistanceFromCentreM = 100 };
        var bot = Bot();

        Assert.False(await new Wanderer(router, options).ExtendAsync(bot, new Random(1), CancellationToken.None));
        Assert.Equal(0, router.Calls);
    }
}

public class SpawnerTests
{
    private static readonly BotDotsOptions Options = new() { TargetPerCell = 3, MaxPerCell = 5, MaxBots = 40, MaxSpawnPerCellPerSync = 2 };

    private static CellInfo Cell(string id, int real, int bots = 0) => new(id, -41.29, 174.78, real, bots);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(9, 0)]
    public void Want_FillsTheGap_UpToTheTarget(int real, int want) =>
        Assert.Equal(want, Spawner.Want(real, Options));

    [Fact]
    public void ACellWithOnePerson_GetsTwoBotDots()
    {
        var plan = Spawner.Plan([Cell("rbsm1", real: 1)], [], Options);

        var spawn = Assert.Single(plan.Spawn);
        Assert.Equal("rbsm1", spawn.Cell.Cell);
        Assert.Equal(2, spawn.Count);
        Assert.Empty(plan.Retire);
    }

    [Fact]
    public void Spawning_IsStaggered_PerSync()
    {
        var options = new BotDotsOptions { TargetPerCell = 5, MaxPerCell = 5, MaxSpawnPerCellPerSync = 2 };

        var plan = Spawner.Plan([Cell("rbsm1", real: 1)], [], options);

        Assert.Equal(2, Assert.Single(plan.Spawn).Count);
    }

    [Fact]
    public void Spawning_StopsAtTheFleetCap()
    {
        var options = new BotDotsOptions { MaxBots = 3 };
        var fleet = new[] { Bot(index: 1, cell: "other"), Bot(index: 2, cell: "other") };

        var plan = Spawner.Plan([Cell("rbsm1", real: 1), Cell("rbsm2", real: 1)], fleet, options);

        Assert.Equal(1, plan.Spawn.Sum(s => s.Count));
    }

    [Fact]
    public void BotDots_AlreadyThere_Count()
    {
        var plan = Spawner.Plan([Cell("rbsm1", real: 1)], [Bot(index: 1, cell: "rbsm1")], Options);

        Assert.Equal(1, Assert.Single(plan.Spawn).Count);
    }

    [Fact]
    public void WhenThePersonLeaves_TheCellsBotDotsRetire()
    {
        var fleet = new[] { Bot(index: 1, cell: "rbsm1"), Bot(index: 2, cell: "rbsm1") };

        var plan = Spawner.Plan([], fleet, Options);

        Assert.Equal(2, plan.Retire.Count);
        Assert.Empty(plan.Spawn);
    }

    [Fact]
    public void WhenRealPeopleArrive_BotDotsThinOut()
    {
        var fleet = new[] { Bot(index: 1, cell: "rbsm1"), Bot(index: 2, cell: "rbsm1") };

        var plan = Spawner.Plan([Cell("rbsm1", real: 2)], fleet, Options);

        Assert.Single(plan.Retire);
        Assert.Empty(plan.Spawn);
    }

    [Fact]
    public void RetiringBotDots_DoNotCountAsCoverage()
    {
        var leaving = Bot(index: 1, cell: "rbsm1");
        leaving.Retiring = true;

        var plan = Spawner.Plan([Cell("rbsm1", real: 1)], [leaving], Options);

        Assert.Equal(2, Assert.Single(plan.Spawn).Count);
        Assert.Empty(plan.Retire);
    }

    [Fact]
    public void ACellWithOnlyBotDots_GetsNoMore()
    {
        var plan = Spawner.Plan([Cell("rbsm1", real: 0, bots: 2)], [], Options);

        Assert.Empty(plan.Spawn);
    }
}
