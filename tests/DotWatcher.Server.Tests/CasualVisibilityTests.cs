using Xunit;

namespace DotWatcher.Server.Tests;

// The pure rules for who can see whom in the casual session. See .ai/plans/casual-tracking.md.
public class CasualVisibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, string> NoCrew = new Dictionary<string, string>();
    private static readonly IReadOnlySet<string> NoBlocks = new HashSet<string>();

    private static CasualLiveEntry Entry(
        string userId,
        double latitude = -41.17,
        double longitude = 174.7762,
        bool visible = true,
        bool inSession = false,
        double? startLatitude = null,
        double? startLongitude = null,
        TimeSpan? age = null,
        DateTimeOffset? nextExpectedAt = null) =>
        new(
            userId,
            new RunnerPosition(userId.ToUpperInvariant(), latitude, longitude, 90, Now - (age ?? TimeSpan.FromSeconds(5)), nextExpectedAt),
            inSession,
            visible,
            startLatitude,
            startLongitude);

    private static CasualLiveResponse View(
        IReadOnlyCollection<CasualLiveEntry> live,
        bool viewerVisible = true,
        (double, double)? viewerPosition = null,
        IReadOnlyDictionary<string, string>? crew = null,
        IReadOnlySet<string>? blocked = null) =>
        CasualVisibility.ForViewer(
            "me", viewerVisible, viewerPosition ?? (-41.17, 174.7762), crew ?? NoCrew, blocked ?? NoBlocks, live, Now);

    [Fact]
    public void Viewer_SeesThemselves_EvenWithNothingElseOn()
    {
        var response = View([Entry("me")], viewerVisible: false);

        Assert.Equal(new[] { "ME" }, response.Participants);
        Assert.True(response.Positions.Single().Single().IsSelf);
    }

    [Fact]
    public void Crew_AreNamed_AtAnyDistance_WhateverTheirToggle()
    {
        var response = View(
            [Entry("amy", latitude: -30, visible: false)],
            crew: new Dictionary<string, string> { ["amy"] = "Aimes" });

        Assert.Equal(new[] { "Aimes" }, response.Participants);
        Assert.Empty(response.Nearby);
    }

    [Fact]
    public void NonCrew_AreOnlyDots_AndOnlyWhenBothVisible()
    {
        var other = Entry("zed", latitude: -41.16);

        Assert.Single(View([other]).Nearby);
        Assert.Empty(View([other], viewerVisible: false).Nearby);
        Assert.Empty(View([Entry("zed", latitude: -41.16, visible: false)]).Nearby);
        Assert.Empty(View([other]).Participants);
    }

    [Fact]
    public void NonCrew_WithoutAViewerPosition_AreHidden()
    {
        var response = CasualVisibility.ForViewer(
            "me", true, null, NoCrew, NoBlocks, [Entry("zed", latitude: -41.16)], Now);

        Assert.Empty(response.Nearby);
    }

    [Theory]
    [InlineData(-41.17 + 0.044, true)]  // about 4.9 km
    [InlineData(-41.17 + 0.046, false)] // about 5.1 km
    public void NonCrew_AreLimitedToFiveKilometres(double latitude, bool shown)
    {
        var response = View([Entry("zed", latitude: latitude)]);

        Assert.Equal(shown, response.Nearby.Count == 1);
    }

    [Theory]
    [InlineData(0.002, false)] // about 220 m from their start
    [InlineData(0.004, true)]  // about 440 m from their start
    public void NonCrew_AreHiddenNearTheirOwnStart(double movedLatitude, bool shown)
    {
        var response = View([Entry("zed", latitude: -41.16 + movedLatitude, startLatitude: -41.16, startLongitude: 174.7762)]);

        Assert.Equal(shown, response.Nearby.Count == 1);
    }

    [Fact]
    public void Crew_AreShownNearTheirStart()
    {
        var response = View(
            [Entry("amy", latitude: -41.16, startLatitude: -41.16, startLongitude: 174.7762)],
            crew: new Dictionary<string, string> { ["amy"] = "AM" });

        Assert.Equal(new[] { "AM" }, response.Participants);
    }

    [Fact]
    public void Blocked_NeverAppear_AsCrewOrDots()
    {
        var blocked = new HashSet<string> { "amy", "zed" };

        var response = View(
            [Entry("amy"), Entry("zed", latitude: -41.16)],
            crew: new Dictionary<string, string> { ["amy"] = "AM" },
            blocked: blocked);

        Assert.Empty(response.Participants);
        Assert.Empty(response.Nearby);
    }

    [Fact]
    public void Dots_CarryTheSessionLock_AndNothingIdentifying()
    {
        var dot = View([Entry("zed", latitude: -41.16, inSession: true)]).Nearby.Single();

        Assert.True(dot.InSession);
        Assert.Equal(
            new[] { "Heading", "InSession", "Latitude", "Longitude" },
            typeof(NearbyDot).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public void IsLive_ExpiresTenMinutesAfterTheLastPosition()
    {
        Assert.True(CasualVisibility.IsLive(Entry("a", age: TimeSpan.FromMinutes(9)).Position, Now));
        Assert.False(CasualVisibility.IsLive(Entry("a", age: TimeSpan.FromMinutes(11)).Position, Now));
    }

    [Fact]
    public void IsLive_HonoursASlowerCadence_UpToThirtyMinutes()
    {
        // Last heard 15 minutes ago, said the next post is due 14 minutes ago + grace, so live.
        var slow = Entry("a", age: TimeSpan.FromMinutes(15), nextExpectedAt: Now - TimeSpan.FromMinutes(1)).Position;
        Assert.True(CasualVisibility.IsLive(slow, Now));

        // A garbled far-future nextExpectedAt can't keep a dot past the thirty minute cap.
        var stuck = Entry("a", age: TimeSpan.FromMinutes(31), nextExpectedAt: Now + TimeSpan.FromHours(3)).Position;
        Assert.False(CasualVisibility.IsLive(stuck, Now));
    }

    [Fact]
    public void StalePositions_AreDropped()
    {
        var response = View([Entry("zed", latitude: -41.16, age: TimeSpan.FromMinutes(20))]);

        Assert.Empty(response.Nearby);
    }
}
