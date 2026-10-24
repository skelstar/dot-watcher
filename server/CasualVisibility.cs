namespace DotWatcher.Server;

/// <summary>
/// Who a viewer can see in the casual session, and how. Pure (no I/O) so the rules live in one place
/// and are unit-testable. See .ai/plans/casual-tracking.md.
///
/// <list type="bullet">
/// <item>The viewer always sees themselves.</item>
/// <item>Crew are named (nickname, else initials) at any distance, whatever their toggle says.</item>
/// <item>Anyone else is an anonymous dot, only if both they and the viewer have the toggle on, they are
/// within <see cref="NearbyRadiusMeters"/> of the viewer, and they are not within
/// <see cref="StartFuzzMeters"/> of where their own run began.</item>
/// <item>Blocked people never appear.</item>
/// </list>
/// </summary>
public static class CasualVisibility
{
    public const double NearbyRadiusMeters = 5000;
    public const double StartFuzzMeters = 300;

    /// <summary>
    /// Whether a position still counts as "out". Ten minutes after the last position, extended to
    /// two minutes past the client's own <c>nextExpectedAt</c> (so a satellite cadence doesn't drop
    /// the dot), never beyond thirty minutes after the position.
    /// </summary>
    public static bool IsLive(RunnerPosition position, DateTimeOffset now)
    {
        var until = position.Timestamp + SessionStore.CasualStaleAfter;
        if (position.NextExpectedAt is { } next)
        {
            var extended = next + SessionStore.CasualNextExpectedGrace;
            if (extended > until)
                until = extended;
        }

        var cap = position.Timestamp + SessionStore.CasualHardStaleCap;
        if (until > cap)
            until = cap;

        return now <= until;
    }

    public static CasualLiveResponse ForViewer(
        string viewerId,
        bool viewerVisibleToNonCrew,
        (double Latitude, double Longitude)? viewerPosition,
        IReadOnlyDictionary<string, string> crewLabels,
        IReadOnlySet<string> blockedUserIds,
        IReadOnlyCollection<CasualLiveEntry> live,
        DateTimeOffset now)
    {
        var participants = new List<string>();
        var positions = new List<CasualPosition[]>();
        var nearby = new List<NearbyDot>();

        foreach (var entry in live)
        {
            if (!IsLive(entry.Position, now))
                continue;

            if (entry.UserId == viewerId)
            {
                participants.Add(entry.Position.RunnerName);
                positions.Add([ToCasualPosition(entry, entry.Position.RunnerName, isSelf: true)]);
                continue;
            }

            if (blockedUserIds.Contains(entry.UserId))
                continue;

            if (crewLabels.TryGetValue(entry.UserId, out var label))
            {
                participants.Add(label);
                positions.Add([ToCasualPosition(entry, label, isSelf: false)]);
                continue;
            }

            if (!viewerVisibleToNonCrew || !entry.VisibleToNonCrew || viewerPosition is not { } here)
                continue;

            var p = entry.Position;
            if (GeoMath.DistanceMeters(here.Latitude, here.Longitude, p.Latitude, p.Longitude) > NearbyRadiusMeters)
                continue;

            if (entry.StartLatitude is { } startLat && entry.StartLongitude is { } startLon
                && GeoMath.DistanceMeters(startLat, startLon, p.Latitude, p.Longitude) < StartFuzzMeters)
                continue;

            nearby.Add(new NearbyDot(p.Latitude, p.Longitude, p.Heading, entry.InSession));
        }

        return new CasualLiveResponse(participants, positions, nearby);
    }

    private static CasualPosition ToCasualPosition(CasualLiveEntry entry, string name, bool isSelf)
    {
        var p = entry.Position;
        return new CasualPosition(
            name, p.Latitude, p.Longitude, p.Heading, p.Timestamp,
            p.NextExpectedAt, p.IsUltraConstrained, p.BatteryLevel, entry.InSession, isSelf);
    }
}

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
