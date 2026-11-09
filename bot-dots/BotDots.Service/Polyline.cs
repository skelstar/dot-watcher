namespace BotDots.Service;

/// <summary>Valhalla returns route shapes as Google-style encoded polylines with 6 decimal places.</summary>
public static class Polyline
{
    public static List<LatLon> Decode6(string encoded)
    {
        var points = new List<LatLon>();
        var index = 0;
        long lat = 0, lon = 0;
        while (index < encoded.Length)
        {
            lat += NextValue(encoded, ref index);
            lon += NextValue(encoded, ref index);
            points.Add(new LatLon(lat / 1e6, lon / 1e6));
        }

        return points;
    }

    private static long NextValue(string text, ref int index)
    {
        var shift = 0;
        long result = 0;
        int chunk;
        do
        {
            chunk = text[index++] - 63;
            result |= (long)(chunk & 0x1F) << shift;
            shift += 5;
        }
        while (chunk >= 0x20);

        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
    }
}
