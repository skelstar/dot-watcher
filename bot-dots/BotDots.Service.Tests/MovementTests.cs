using Xunit;

namespace BotDots.Service.Tests;

public class MovementTests
{
    internal static readonly LatLon Centre = new(-41.2865, 174.7762);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);
    private static readonly Persona SixMinuteKm = new("test", 360, 360, 1); // 2.78 m/s

    internal static BotRunner Bot(LatLon? start = null, int index = 1, string cell = "rbsm1", DateTimeOffset? endsAt = null, int seed = 1) =>
        new(index, SixMinuteKm, cell, Centre, start ?? Centre, endsAt ?? Now.AddMinutes(30), new Random(seed));

    // A straight line due north, made of several points so it looks like a routed shape.
    internal static List<LatLon> North(LatLon from, double meters, int points = 10) =>
        Enumerable.Range(0, points + 1).Select(i => Geo.Destination(from, 0, meters * i / points)).ToList();

    [Fact]
    public void FirstRoute_SnapsTheBotDotOntoItsStart()
    {
        var bot = Bot(start: Geo.Destination(Centre, 90, 30));
        var route = North(Centre, 1000);

        bot.AppendRoute(route);

        Assert.Equal(route[0], bot.Position);
        Assert.InRange(bot.RemainingMeters, 995, 1005);
    }

    [Fact]
    public void Advance_MovesAtThePersonasPace_GiveOrTakeTheDrift()
    {
        var bot = Bot();
        bot.AppendRoute(North(Centre, 1000));

        bot.Advance(100);

        // 2.78 m/s for 100 s, with at most 10% drift.
        Assert.InRange(Geo.DistanceMeters(Centre, bot.Position), 250, 306);
        Assert.InRange(Geo.AngleDifference(bot.Heading, 0), 0, 1);
    }

    [Fact]
    public void Advance_StopsAtTheEndOfThePath()
    {
        var bot = Bot();
        var route = North(Centre, 200);
        bot.AppendRoute(route);

        bot.Advance(600);

        Assert.Equal(0, bot.RemainingMeters);
        Assert.InRange(Geo.DistanceMeters(route[^1], bot.Position), 0, 0.5);
    }

    [Fact]
    public void AppendRoute_JoinsTheNextHop_AndDropsWhatWasWalked()
    {
        var bot = Bot();
        var first = North(Centre, 500);
        bot.AppendRoute(first);
        bot.Advance(150); // about 417 m along

        bot.AppendRoute(North(first[^1], 500));

        Assert.InRange(bot.RemainingMeters, 500 + 500 - 460, 500 + 500 - 375);
    }

    [Fact]
    public void NoisyPosition_StaysWithinAFewMetres()
    {
        var bot = Bot();
        bot.AppendRoute(North(Centre, 1000));
        for (var i = 0; i < 200; i++)
            Assert.InRange(Geo.DistanceMeters(bot.Position, bot.NoisyPosition()), 0, 25);
    }

    [Fact]
    public void Overlap_CountsGroundAlreadyCovered()
    {
        var bot = Bot();
        var route = North(Centre, 1000, points: 50);
        bot.AppendRoute(route);
        for (var i = 0; i < 30; i++)
            bot.Advance(15);

        Assert.True(bot.OverlapFraction(route) > 0);
        Assert.Equal(0, bot.OverlapFraction(North(Geo.Destination(Centre, 90, 3000), 1000, points: 50)));
    }

    [Fact]
    public void Username_UsesTheServersReservedPrefix()
    {
        Assert.Equal("botdot-007", Bot(index: 7).Username);
    }

    [Fact]
    public void Persona_Pick_ReturnsEveryTypeEventually_AndSpeedsAreSensible()
    {
        var rng = new Random(3);
        var seen = Enumerable.Range(0, 500).Select(_ => Persona.Pick(rng).Name).ToHashSet();

        Assert.Equal(Persona.All.Select(p => p.Name).ToHashSet(), seen);
        foreach (var persona in Persona.All)
            Assert.InRange(persona.PickSpeed(rng), 1.2, 3.6);
    }
}
