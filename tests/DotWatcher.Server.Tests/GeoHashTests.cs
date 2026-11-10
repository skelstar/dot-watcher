using Xunit;

namespace DotWatcher.Server.Tests;

// The geohash cells the bot-dots service is told about (see .ai/plans/bot-dot-army.md).
public class GeoHashTests
{
    [Fact]
    public void Encode_MatchesTheWellKnownReferenceHash()
    {
        // The standard example from the geohash specification and Wikipedia.
        Assert.Equal("u4pruydqqvj", GeoHash.Encode(57.64911, 10.40744, 11));
        Assert.Equal("u4pru", GeoHash.Encode(57.64911, 10.40744, 5));
    }

    [Fact]
    public void DecodeCentre_IsInsideTheCell_AndEncodesBackToIt()
    {
        foreach (var (lat, lon) in new[] { (-41.2865, 174.7762), (51.5074, -0.1278), (0.0, 0.0), (-89.9, 179.9) })
        {
            var cell = GeoHash.Encode(lat, lon, BotDots.CellPrecision);
            var centre = GeoHash.DecodeCentre(cell)!.Value;

            Assert.Equal(cell, GeoHash.Encode(centre.Latitude, centre.Longitude, BotDots.CellPrecision));
            // A 6-character cell is about 1.2 km by 0.6 km, so no point is further than ~700 m from its centre.
            Assert.True(GeoMath.DistanceMeters(lat, lon, centre.Latitude, centre.Longitude) < 750);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")] // 'a' is not in the geohash alphabet
    public void DecodeCentre_RejectsInvalidText(string? text) =>
        Assert.Null(GeoHash.DecodeCentre(text));

    [Fact]
    public void IsBotDotUsername_MatchesThePrefix_IgnoringCase()
    {
        Assert.True(BotDots.IsBotDotUsername("botdot-001"));
        Assert.True(BotDots.IsBotDotUsername("BotDot-001"));
        Assert.False(BotDots.IsBotDotUsername("botdot"));
        Assert.False(BotDots.IsBotDotUsername("my-botdot-1"));
        Assert.False(BotDots.IsBotDotUsername(null));
    }
}
