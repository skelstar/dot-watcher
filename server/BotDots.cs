namespace DotWatcher.Server;

/// <summary>
/// Bot-dots (robot runners run by the separate bot-dots service). A bot-dot is an ordinary account whose
/// username starts with <see cref="UsernamePrefix"/>: it signs in and posts like a phone, so the
/// casual rules apply to it unchanged. See .ai/plans/bot-dot-army.md.
/// </summary>
public static class BotDots
{
    /// <summary>Reserved. <c>/auth/register</c> refuses it unless the admin bearer is sent and bot-dots are enabled.</summary>
    public const string UsernamePrefix = "botdot-";

    /// <summary>How long bot-dot positions are kept (the maintenance job runs hourly, so up to about an hour more).
    /// Nobody replays them, and 40 bot-dots posting every 15 s would otherwise add ~230k rows a day.</summary>
    public static readonly TimeSpan PositionRetention = TimeSpan.FromHours(1);

    /// <summary>Length of the geohash cells <c>GET /admin/casual/cells</c> reports (about 5 km square).</summary>
    public const int CellPrecision = 5;

    public static bool IsBotDotUsername(string? username) =>
        username is not null && username.StartsWith(UsernamePrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Standard geohash (base 32, longitude bit first), used so the bot-dots service learns only coarse areas.</summary>
public static class GeoHash
{
    private const string Alphabet = "0123456789bcdefghjkmnpqrstuvwxyz";

    public static string Encode(double latitude, double longitude, int precision)
    {
        double latMin = -90, latMax = 90, lonMin = -180, lonMax = 180;
        var chars = new char[precision];
        var evenBit = true; // even bits refine longitude
        var bit = 0;
        var index = 0;
        var position = 0;
        while (position < precision)
        {
            if (evenBit)
            {
                var mid = (lonMin + lonMax) / 2;
                if (longitude >= mid) { index = (index << 1) | 1; lonMin = mid; }
                else { index <<= 1; lonMax = mid; }
            }
            else
            {
                var mid = (latMin + latMax) / 2;
                if (latitude >= mid) { index = (index << 1) | 1; latMin = mid; }
                else { index <<= 1; latMax = mid; }
            }

            evenBit = !evenBit;
            if (++bit == 5)
            {
                chars[position++] = Alphabet[index];
                bit = 0;
                index = 0;
            }
        }

        return new string(chars);
    }

    /// <summary>The centre of a cell, or null if the text isn't a valid geohash.</summary>
    public static (double Latitude, double Longitude)? DecodeCentre(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
            return null;

        double latMin = -90, latMax = 90, lonMin = -180, lonMax = 180;
        var evenBit = true;
        foreach (var c in hash)
        {
            var value = Alphabet.IndexOf(char.ToLowerInvariant(c));
            if (value < 0)
                return null;

            for (var mask = 16; mask > 0; mask >>= 1)
            {
                var set = (value & mask) != 0;
                if (evenBit)
                {
                    var mid = (lonMin + lonMax) / 2;
                    if (set) lonMin = mid; else lonMax = mid;
                }
                else
                {
                    var mid = (latMin + latMax) / 2;
                    if (set) latMin = mid; else latMax = mid;
                }

                evenBit = !evenBit;
            }
        }

        return ((latMin + latMax) / 2, (lonMin + lonMax) / 2);
    }
}
