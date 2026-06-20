using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LocationsController(
    SessionStore store,
    UserTokenAuth userAuth,
    ILogger<LocationsController> logger) : ControllerBase
{
    [HttpPost("/location")]
    public IActionResult AddLocation([FromBody] LocationUpdate update)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var sessionCode = SessionStore.NormalizeSessionCode(update.SessionCode);
        if (sessionCode is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.CanWriteLocation(sessionCode, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        var membership = store.GetMembership(sessionCode, user.UserId)!;
        var storedUpdate = update with
        {
            RunnerName = membership.DisplayName,
            SessionCode = sessionCode,
        };

        store.AddPosition(storedUpdate);
        logger.LogInformation("[{Session}] {Runner} → {Lat:F6}, {Lon:F6}  heading={Heading}  t={Timestamp:HH:mm:ss}",
            storedUpdate.SessionCode, storedUpdate.RunnerName,
            storedUpdate.Latitude, storedUpdate.Longitude,
            storedUpdate.Heading.HasValue ? $"{storedUpdate.Heading:F1}°" : "n/a",
            storedUpdate.Timestamp);
        var participants = store.GetParticipants(storedUpdate.SessionCode);
        return Ok(new { participants });
    }

    [HttpGet("/locations/{sessionCode}")]
    public IActionResult GetLatestPositions(string sessionCode)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.CanReadSession(code, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return Ok(store.GetLatestPositions(code));
    }
}
