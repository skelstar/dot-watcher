namespace BotDots.Service;

public readonly record struct LatLon(double Lat, double Lon);

/// <summary>Small spherical-earth helpers. Plenty accurate for tens of kilometres.</summary>
public static class Geo
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(LatLon a, LatLon b)
    {
        var dLat = ToRadians(b.Lat - a.Lat);
        var dLon = ToRadians(b.Lon - a.Lon);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(a.Lat)) * Math.Cos(ToRadians(b.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Initial compass bearing from a to b, 0-360 (0 = north).</summary>
    public static double BearingDegrees(LatLon a, LatLon b)
    {
        var lat1 = ToRadians(a.Lat);
        var lat2 = ToRadians(b.Lat);
        var dLon = ToRadians(b.Lon - a.Lon);
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return NormalizeDegrees(ToDegrees(Math.Atan2(y, x)));
    }

    public static LatLon Destination(LatLon from, double bearingDegrees, double distanceMeters)
    {
        var bearing = ToRadians(bearingDegrees);
        var d = distanceMeters / EarthRadiusMeters;
        var lat1 = ToRadians(from.Lat);
        var lon1 = ToRadians(from.Lon);
        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(bearing));
        var lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(d) * Math.Cos(lat1),
            Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        return new LatLon(ToDegrees(lat2), ToDegrees(lon2));
    }

    public static double NormalizeDegrees(double degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>The smaller angle between two bearings, 0-180.</summary>
    public static double AngleDifference(double a, double b)
    {
        var d = Math.Abs(NormalizeDegrees(a) - NormalizeDegrees(b));
        return d > 180 ? 360 - d : d;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
