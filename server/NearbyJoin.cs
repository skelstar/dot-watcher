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

    /// <summary>How much of each fix's error is forgiven when judging the radius.</summary>
    public const double MaxForgivenAccuracyMeters = 50;

    /// <summary>The most sessions one list request returns.</summary>
    public const int MaxListed = 10;

    /// <summary>Distances shown to people are rounded to this, never the real figure.</summary>
    public const int DistanceRoundingMeters = 50;

    /// <summary>
    /// Whether a person is close enough to a meeting point. Phone GPS is off by 5-50 m and both fixes
    /// are, so each fix's accuracy is forgiven (capped, so a terrible fix can't open the gate wide).
    /// A missing accuracy forgives nothing.
    /// </summary>
    public static bool IsWithinRadius(double distanceMeters, int radiusMeters, double? joinerAccuracy, double? anchorAccuracy) =>
        distanceMeters - Forgiven(joinerAccuracy) - Forgiven(anchorAccuracy) <= radiusMeters;

    public static int RoundDistance(double distanceMeters) =>
        (int)(Math.Round(distanceMeters / DistanceRoundingMeters, MidpointRounding.AwayFromZero) * DistanceRoundingMeters);

    private static double Forgiven(double? accuracy) =>
        accuracy is double a ? Math.Clamp(a, 0, MaxForgivenAccuracyMeters) : 0;

    public static bool IsValidPosition(double? latitude, double? longitude, double? accuracy) =>
        latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180
        && (accuracy is null or (>= 0 and <= MaxStoredAccuracyMeters));
}

/// <summary>A GPS fix used as a session's meeting point. Never returned to clients.</summary>
public record MeetingPoint(double Latitude, double Longitude, double? AccuracyMeters);

public enum NearbyUpdateOutcome { Ok, NotOwner, Archived }

/// <summary>What a client is told about a session's nearby setting (no coordinates).</summary>
public record NearbyStatus(bool Nearby, int RadiusMeters);

/// <summary>A session's nearby settings and meeting point, as supplied at create time.</summary>
public record NearbySettings(int RadiusMeters, double Latitude, double Longitude, double? AccuracyMeters);
