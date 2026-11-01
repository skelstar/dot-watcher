using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// Events near you. With a position it lists the sessions people nearby can join (see
/// .ai/plans/nearby-join.md). Without one it still returns the old placeholder, so builds that
/// predate nearby join keep showing their sign.
/// </summary>
[ApiController]
public class EventsController(UserTokenAuth userAuth, SessionStore store, NearbyLimiter limiter, TimeProvider timeProvider)
    : ControllerBase
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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetNearby([FromQuery] double? lat, [FromQuery] double? lon, [FromQuery] double? accuracy)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (lat is null && lon is null)
            return Ok(Placeholder);

        // Only real lookups count; the position-less placeholder call from older builds is never limited.
        if (!limiter.TryAcceptList(user.UserId))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many requests. Try again later." });

        if (lat is not double latitude || lon is not double longitude
            || !NearbyJoin.IsValidPosition(latitude, longitude, null) || accuracy is < 0)
            return BadRequest(new { error = "lat and lon must be a valid position, and accuracy cannot be negative." });

        var sessions = store.FindNearbySessions(user.UserId, latitude, longitude, accuracy, timeProvider.GetUtcNow());
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
