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
/// <see cref="StartFuzzMeters"/> of where their own run began (unless they turned
/// <see cref="CasualLiveEntry.HideStartArea"/> off).</item>
/// <item>Blocked people never appear.</item>
/// </list>
/// </summary>
public static class CasualVisibility
{
    public const double NearbyRadiusMeters = 5000;
    public const double StartFuzzMeters = 300;

    /// <summary>
    /// Whether someone still counts as "out": ten minutes after the server last heard from them,
    /// extended to the client's own posting interval (nextExpectedAt minus the position's time) plus a
    /// grace so a slow satellite cadence doesn't drop the dot, never beyond thirty minutes. Judged
    /// entirely on the server's clock (<see cref="CasualLiveEntry.ReceivedAt"/>): the interval is a
    /// difference between two phone times, so a wrong phone clock cancels out.
    /// </summary>
    public static bool IsLive(CasualLiveEntry entry, DateTimeOffset now)
    {
        var window = SessionStore.CasualStaleAfter;
        var position = entry.Position;
        if (position.NextExpectedAt is { } next && next > position.Timestamp)
        {
            var extended = (next - position.Timestamp) + SessionStore.CasualNextExpectedGrace;
            if (extended > window)
                window = extended;
        }

        if (window > SessionStore.CasualHardStaleCap)
            window = SessionStore.CasualHardStaleCap;

        return now - entry.ReceivedAt <= window;
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
        var positions = new List<RunnerPosition[]>();
        var nearby = new List<NearbyDot>();

        foreach (var entry in live)
        {
            if (!IsLive(entry, now))
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

            if (entry.HideStartArea && entry.StartLatitude is { } startLat && entry.StartLongitude is { } startLon
                && GeoMath.DistanceMeters(startLat, startLon, p.Latitude, p.Longitude) < StartFuzzMeters)
                continue;

            nearby.Add(new NearbyDot(p.Latitude, p.Longitude, p.Heading, entry.InSession));
        }

        return new CasualLiveResponse(participants, positions, nearby);
    }

    /// <summary>The stored position, labelled for this viewer. RunnerName at rest is the account's initials.</summary>
    private static RunnerPosition ToCasualPosition(CasualLiveEntry entry, string name, bool isSelf) =>
        entry.Position with
        {
            RunnerName = name,
            InSession = entry.InSession,
            IsSelf = isSelf,
            DisplayName = entry.Position.RunnerName,
        };
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
