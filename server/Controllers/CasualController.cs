using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// Casual tracking: start tracking without creating or joining a session. Crew see you by name,
/// others nearby as anonymous dots. See .ai/plans/casual-tracking.md.
/// </summary>
[ApiController]
public class CasualController(
    SessionStore store,
    UserTokenAuth userAuth,
    FeatureFlags features,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>
    /// Starts the caller's casual run (or continues the one they have) and returns the session id to
    /// post positions to with POST /location. Safe to call on every launch.
    /// </summary>
    [HttpPost("/casual/start")]
    [ProducesResponseType(typeof(CasualStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Start()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return Ok(store.StartCasualRun(user.UserId, timeProvider.GetUtcNow()));
    }

    /// <summary>Ends the caller's casual run. They leave everyone's map at once. Safe to repeat.</summary>
    [HttpPost("/casual/stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Stop()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        store.StopCasualRun(user.UserId, timeProvider.GetUtcNow());
        return NoContent();
    }

    /// <summary>
    /// Who the caller can see: themselves and their crew by name, plus, only while the caller is out
    /// themselves, anonymous dots within 5 km of where the server last placed them. Someone not
    /// tracking (just watching) gets their crew only; the caller cannot supply a position to look
    /// around, so the map can't be scanned for strangers.
    /// </summary>
    [HttpGet("/casual/live")]
    [ProducesResponseType(typeof(CasualLiveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLive()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        var account = store.GetCasualAccount(user.UserId);
        return Ok(store.GetCasualLive(
            user.UserId,
            account.VisibleToNonCrew,
            features.ForGranted(account.GrantedFeatures).Contains(Features.Crew),
            self: null,
            timeProvider.GetUtcNow()));
    }

    /// <summary>Gets whether people who aren't in the caller's crew can see them (off by default).</summary>
    [HttpGet("/me/casual/settings")]
    [ProducesResponseType(typeof(CasualSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetSettings()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return Ok(new CasualSettings(store.GetCasualVisibleToNonCrew(user.UserId)));
    }

    /// <summary>Sets whether people who aren't in the caller's crew can see them. Omitted fields are left alone.</summary>
    [HttpPut("/me/casual/settings")]
    [ProducesResponseType(typeof(CasualSettings), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UpdateSettings([FromBody] UpdateCasualSettingsRequest? request)
    {
        if (Gate(out var user) is { } denied)
            return denied;

        if (request?.VisibleToNonCrew is { } visible)
            store.SetCasualVisibleToNonCrew(user.UserId, visible);

        return Ok(new CasualSettings(store.GetCasualVisibleToNonCrew(user.UserId)));
    }

    // 401 when not signed in. 404 (not 403) when the account doesn't have casual yet, so the
    // endpoints look absent to a build that doesn't know about them - same rule as crew.
    private IActionResult? Gate(out AuthenticatedUser user)
    {
        if (!userAuth.TryAuthenticate(Request, out user))
            return Unauthorized();

        return features.IsEnabled(user.UserId, Features.Casual) ? null : NotFound();
    }
}
