using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LocationsController(
    SessionStore store,
    UserTokenAuth userAuth,
    FeatureFlags features,
    TimeProvider timeProvider,
    ILogger<LocationsController> logger) : ControllerBase
{
    /// <summary>Records a runner's position and returns the session's active participant names plus latest positions.</summary>
    [HttpPost("/location")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult AddLocation([FromBody] LocationUpdate? update)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (update is null)
            return BadRequest(new { error = "Request body is required." });

        var sessionId = update.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        // Casual has no memberships (see SessionStore.Casual.cs), so it is handled before the
        // membership checks below, which would refuse it.
        if (store.IsCasualSession(sessionId))
            return AddCasualLocation(update, user);

        if (!store.CanWriteLocation(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        var membership = store.GetMembership(sessionId, user.UserId)!;
        var storedUpdate = update with
        {
            RunnerName = membership.DisplayName,
            SessionId = sessionId,
        };

        if (!LocationUpdateValidation.TryValidate(storedUpdate, out var validatedUpdate, out var errors))
            return BadRequest(new { error = "Invalid location update.", details = errors });

        store.AddPosition(validatedUpdate, user.UserId);
        CopyToCasual(validatedUpdate, user);
        HttpContext.Items["Log:Session"] = validatedUpdate.SessionId;
        HttpContext.Items["Log:Runner"] = validatedUpdate.RunnerName;
        HttpContext.Items["Log:LocationTimestamp"] = validatedUpdate.Timestamp.ToString("HH:mm:ss");
        if (store.GetInviteCodeBySessionId(validatedUpdate.SessionId) is string inviteCode)
            HttpContext.Items["Log:Code"] = inviteCode;
        var participants = store.GetParticipants(validatedUpdate.SessionId, user.UserId);
        var positions = store.GetLatestPositions(validatedUpdate.SessionId, user.UserId);
        return Ok(new { participants, positions });
    }

    // A runner in a named session who has chosen to be visible to non-crew is also out in casual,
    // marked as being in a session (the lock). Both settings are off for everyone until they turn
    // them on, so accounts on an older build never take this path. Casual must never break the
    // named session's own post, hence the catch.
    private void CopyToCasual(ValidatedLocationUpdate update, AuthenticatedUser user)
    {
        if (update.SessionId == store.DemoSessionId)
            return;

        try
        {
            if (!store.GetCasualVisibleToNonCrew(user.UserId) || !features.IsEnabled(user.UserId, Features.Casual))
                return;

            store.AddCasualPosition(
                update with { SessionId = SessionStore.CasualSessionId, RunnerName = user.DisplayName },
                user.UserId,
                inSession: true,
                startRunIfNone: true,
                timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not copy a position into the casual session");
        }
    }

    private IActionResult AddCasualLocation(LocationUpdate update, AuthenticatedUser user)
    {
        if (!features.IsEnabled(user.UserId, Features.Casual))
            return StatusCode(StatusCodes.Status403Forbidden);

        var storedUpdate = update with
        {
            RunnerName = user.DisplayName,
            SessionId = SessionStore.CasualSessionId,
        };

        if (!LocationUpdateValidation.TryValidate(storedUpdate, out var validatedUpdate, out var errors))
            return BadRequest(new { error = "Invalid location update.", details = errors });

        var now = timeProvider.GetUtcNow();
        var self = store.AddCasualPosition(validatedUpdate, user.UserId, inSession: false, startRunIfNone: false, now);
        if (self is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Casual tracking isn't started." });

        HttpContext.Items["Log:Session"] = "casual";
        HttpContext.Items["Log:Runner"] = validatedUpdate.RunnerName;
        HttpContext.Items["Log:LocationTimestamp"] = validatedUpdate.Timestamp.ToString("HH:mm:ss");

        return Ok(store.GetCasualLive(
            user.UserId,
            features.IsEnabled(user.UserId, Features.Crew),
            (validatedUpdate.Latitude, validatedUpdate.Longitude),
            self,
            now));
    }

    /// <summary>Gets the latest live position per runner for a session.</summary>
    [HttpGet("/locations/{sessionId}")]
    [ProducesResponseType(typeof(IReadOnlyList<RunnerPosition[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetLatestPositions(string sessionId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        if (!store.CanReadSession(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return Ok(store.GetLatestPositions(sessionId, user.UserId));
    }
}
