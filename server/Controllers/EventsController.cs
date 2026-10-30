using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// Events near you. With a position it lists the sessions people nearby can join (see
/// .ai/plans/nearby-join.md). Without one it still returns the old placeholder, so builds that
/// predate nearby join keep showing their sign.
/// </summary>
[ApiController]
public class EventsController(UserTokenAuth userAuth, FeatureFlags features, SessionStore store) : ControllerBase
{
    private static readonly NearbyEvent[] Placeholder =
    [
        new("placeholder-rogaine", "Happening near you · 2.3 km", "Rogaine event", "Sat 9am · Hunua Ranges · 14 joined"),
    ];

    /// <summary>The events near the caller, nearest first.</summary>
    [HttpGet("/me/events/nearby")]
    [ProducesResponseType(typeof(IReadOnlyList<NearbyEvent>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetNearby([FromQuery] double? lat, [FromQuery] double? lon, [FromQuery] double? accuracy)
    {
        // 401 when not signed in. 404 (not 403) when the account doesn't have events, so the endpoint
        // looks absent to a build that doesn't know about it - same rule as crew and casual.
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (!features.IsEnabled(user.UserId, Features.Events))
            return NotFound();

        if (lat is null && lon is null)
            return Ok(Placeholder);

        if (lat is not double latitude || lon is not double longitude
            || !NearbyJoin.IsValidPosition(latitude, longitude, null) || accuracy is < 0)
            return BadRequest(new { error = "lat and lon must be a valid position, and accuracy cannot be negative." });

        var sessions = store.FindNearbySessions(user.UserId, latitude, longitude, accuracy, DateTimeOffset.UtcNow);
        var events = sessions.Select(match =>
        {
            var rounded = NearbyJoin.RoundDistance(match.DistanceMeters);
            var away = rounded == 0 ? "here" : $"{rounded} m";
            return new NearbyEvent(
                $"session-{match.SessionId}",
                $"Happening near you · {away}",
                match.Title,
                $"Organised by {match.OrganiserName}",
                match.SessionId);
        }).ToList();

        return Ok(events);
    }
}
