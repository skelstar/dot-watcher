using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// Public events near you. Placeholder for now: accounts with the "events" feature get one made-up
/// event so the apps' start screen can show its sign. There is no event data behind it yet.
/// </summary>
[ApiController]
public class EventsController(UserTokenAuth userAuth, FeatureFlags features) : ControllerBase
{
    private static readonly NearbyEvent[] Placeholder =
    [
        new("placeholder-rogaine", "Happening near you · 2.3 km", "Rogaine event", "Sat 9am · Hunua Ranges · 14 joined"),
    ];

    /// <summary>The events near the caller, nearest first.</summary>
    [HttpGet("/me/events/nearby")]
    [ProducesResponseType(typeof(IReadOnlyList<NearbyEvent>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetNearby()
    {
        // 401 when not signed in. 404 (not 403) when the account doesn't have events, so the endpoint
        // looks absent to a build that doesn't know about it - same rule as crew and casual.
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return features.IsEnabled(user.UserId, Features.Events) ? Ok(Placeholder) : NotFound();
    }
}
