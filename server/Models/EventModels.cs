namespace DotWatcher.Server;

// Public events near you (see .ai/mockups/landing-signs.html). Only reached by accounts with the
// "events" feature, so none of it changes what an installed build already sees.

/// <summary>
/// An event shown on the start screen. <c>Eyebrow</c> is the small line above the title. <c>SessionId</c>
/// is set when the entry is a session people nearby can join (see .ai/plans/nearby-join.md); it is
/// absent on the placeholder, which can't be joined.
/// </summary>
public record NearbyEvent(
    string Id,
    string Eyebrow,
    string Title,
    string Subtitle,
    string? SessionId = null
);

/// <summary>A joinable nearby session as found by the store. Distance is the real figure; the
/// controller rounds it before it reaches a client.</summary>
public record NearbySessionMatch(string SessionId, string Title, string OrganiserName, double DistanceMeters);
