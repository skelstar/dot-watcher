using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// The public side of a casual watch link: anyone holding the link can see one runner's current
/// position, and nothing else. No sign-in; the secret in the path is the whole credential. See
/// .ai/plans/casual-tracking.md.
/// </summary>
[ApiController]
public class WatchController(
    SessionStore store,
    WatchLinkLimiter limiter,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Where the runner behind a watch link is now, by initials. 404 for a link that doesn't exist or was revoked.</summary>
    [HttpGet("/watch/{token}")]
    [ProducesResponseType(typeof(CasualWatchView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Get(string token)
    {
        // The response is a live position and must never be served from a cache.
        Response.Headers.CacheControl = "no-store";

        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var view = SessionStore.LooksLikeWatchToken(token)
            ? store.GetCasualWatchView(token, timeProvider.GetUtcNow())
            : null;

        if (view is not null)
            return Ok(view);

        if (limiter.IsLimited(key))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Try again later." });

        limiter.RecordMiss(key);
        return NotFound();
    }
}
