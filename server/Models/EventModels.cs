namespace DotWatcher.Server;

// Public events near you (see .ai/mockups/landing-signs.html). Only reached by accounts with the
// "events" feature, so none of it changes what an installed build already sees.

/// <summary>
/// An event shown on the start screen. <c>Eyebrow</c> is the small line above the title. <c>SessionId</c>
/// is set when the entry is a session people nearby can join (see .ai/plans/nearby-join.md); it is
/// absent on the placeholder, which can't be joined. <c>Organiser</c> is the organiser's display name (their
/// initials, by design), which newer apps draw as a small dot after "Organised by"; <c>Subtitle</c> still
/// carries "Organised by …" as text for builds that don't know the field. <c>NeedsCode</c> is true when
/// joining needs the organiser's entry code (see .ai/plans/go-and-groups.md); it is only ever sent to
/// accounts with the "groups" feature, and left out (null) otherwise; so is <c>EndsAt</c>, when the group ends.
/// </summary>
public record NearbyEvent(
    string Id,
    string Eyebrow,
    string Title,
    string Subtitle,
    string? SessionId = null,
    string? Organiser = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? NeedsCode = null,
    // ISO-8601 UTC time the group ends. Like NeedsCode, only sent to accounts with "groups".
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? EndsAt = null
);

/// <summary>A joinable nearby session as found by the store. Distance is the real figure; the
/// controller rounds it before it reaches a client.</summary>
public record NearbySessionMatch(
    string SessionId, string Title, string OrganiserName, double DistanceMeters, bool NeedsCode = false,
    DateTimeOffset? EndsAt = null);
