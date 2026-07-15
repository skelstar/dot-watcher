using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LocationsController(
    SessionStore store,
    UserTokenAuth userAuth) : ControllerBase
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
        HttpContext.Items["Log:Session"] = validatedUpdate.SessionId;
        HttpContext.Items["Log:Runner"] = validatedUpdate.RunnerName;
        HttpContext.Items["Log:LocationTimestamp"] = validatedUpdate.Timestamp.ToString("HH:mm:ss");
        var participants = store.GetParticipants(validatedUpdate.SessionId);
        var positions = store.GetLatestPositions(validatedUpdate.SessionId);
        return Ok(new { participants, positions });
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

        return Ok(store.GetLatestPositions(sessionId));
    }
}
