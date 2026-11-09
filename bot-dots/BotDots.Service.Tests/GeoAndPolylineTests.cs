using System.Text;
using Xunit;

namespace BotDots.Service.Tests;

public class GeoAndPolylineTests
{
    private static readonly LatLon Wellington = new(-41.2865, 174.7762);

    [Fact]
    public void Distance_OfAHundredthOfADegreeOfLatitude_IsAboutOnePointOneKilometres()
    {
        var d = Geo.DistanceMeters(Wellington, Wellington with { Lat = Wellington.Lat + 0.01 });

        Assert.InRange(d, 1105, 1120);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(225)]
    public void Destination_ThenDistanceAndBearing_RoundTrip(double bearing)
    {
        var there = Geo.Destination(Wellington, bearing, 1500);

        Assert.InRange(Geo.DistanceMeters(Wellington, there), 1499, 1501);
        Assert.InRange(Geo.AngleDifference(Geo.BearingDegrees(Wellington, there), bearing), 0, 0.5);
    }

    [Fact]
    public void AngleDifference_WrapsAroundNorth()
    {
        Assert.Equal(20, Geo.AngleDifference(350, 10), 6);
        Assert.Equal(180, Geo.AngleDifference(0, 180), 6);
    }

    [Fact]
    public void Decode6_ReadsWhatEncode6Wrote()
    {
        var points = new[] { new LatLon(-41.2865, 174.7762), new LatLon(-41.287001, 174.777999), new LatLon(-41.29, 174.78) };

        var decoded = Polyline.Decode6(Encode6(points));

        Assert.Equal(points.Length, decoded.Count);
        for (var i = 0; i < points.Length; i++)
        {
            Assert.Equal(points[i].Lat, decoded[i].Lat, 6);
            Assert.Equal(points[i].Lon, decoded[i].Lon, 6);
        }
    }

    // The standard polyline encoding, precision 6 (as Valhalla writes it), for the round-trip test.
    private static string Encode6(IEnumerable<LatLon> points)
    {
        var text = new StringBuilder();
        long lastLat = 0, lastLon = 0;
        foreach (var p in points)
        {
            var lat = (long)Math.Round(p.Lat * 1e6);
            var lon = (long)Math.Round(p.Lon * 1e6);
            Write(text, lat - lastLat);
            Write(text, lon - lastLon);
            lastLat = lat;
            lastLon = lon;
        }

        return text.ToString();
    }

    private static void Write(StringBuilder text, long value)
    {
        var v = value < 0 ? ~(value << 1) : value << 1;
        while (v >= 0x20)
        {
            text.Append((char)((0x20 | (v & 0x1F)) + 63));
            v >>= 5;
        }

        text.Append((char)(v + 63));
    }
}
