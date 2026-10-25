namespace DotWatcher.Server;

// Public events near you (see .ai/mockups/landing-signs.html). Only reached by accounts with the
// "events" feature, so none of it changes what an installed build already sees.

/// <summary>An event shown on the start screen. <c>Eyebrow</c> is the small line above the title.</summary>
public record NearbyEvent(
    string Id,
    string Eyebrow,
    string Title,
    string Subtitle
);
