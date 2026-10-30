namespace DotWatcher.Server;

/// <summary>
/// Rules for sessions that people nearby can find and join. See .ai/plans/nearby-join.md.
/// </summary>
public static class NearbyJoin
{
    public const int DefaultRadiusMeters = 100;

    /// <summary>Shown to strangers, so kept short.</summary>
    public const int MaxTitleLength = 30;

    /// <summary>A meeting-point fix worse than this is rejected rather than stored.</summary>
    public const double MaxStoredAccuracyMeters = 100;

    private static readonly int[] AllowedRadii = [50, 100, 250, 500];

    public static bool IsAllowedRadius(int radiusMeters) => AllowedRadii.Contains(radiusMeters);

    /// <summary>
    /// The public title of a nearby session: trimmed, 1-30 characters, no control characters. Unlike the
    /// legacy session name it keeps its case and may contain spaces; it is not the invite code.
    /// </summary>
    public static string? NormalizeTitle(string? value)
    {
        var title = value?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
            return null;

        return title.Any(char.IsControl) ? null : title;
    }

    public static bool IsValidPosition(double? latitude, double? longitude, double? accuracy) =>
        latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180
        && (accuracy is null or (>= 0 and <= MaxStoredAccuracyMeters));
}

/// <summary>A session's nearby settings and meeting point, as supplied at create time.</summary>
public record NearbySettings(int RadiusMeters, double Latitude, double Longitude, double? AccuracyMeters);
