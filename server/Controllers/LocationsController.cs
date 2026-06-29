using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LocationsController(
    SessionStore store,
    UserTokenAuth userAuth) : ControllerBase
{
    [HttpPost("/location")]
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
        HttpContext.Items["Log:Heading"] = validatedUpdate.Heading.HasValue ? $"{validatedUpdate.Heading:F1}°" : "n/a";
        HttpContext.Items["Log:LocationTimestamp"] = validatedUpdate.Timestamp.ToString("HH:mm:ss");
        var participants = store.GetParticipants(validatedUpdate.SessionId);
        var pendingJoinRequests = store.GetPendingJoinRequestCount(validatedUpdate.SessionId);
        return Ok(new { participants, pendingJoinRequests });
    }

    [HttpGet("/locations/{sessionId}")]
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
