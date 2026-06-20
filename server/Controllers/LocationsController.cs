using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LocationsController(
    SessionStore store,
    UserTokenAuth userAuth,
    ILogger<LocationsController> logger) : ControllerBase
{
    [HttpPost("/location")]
    public IActionResult AddLocation([FromBody] LocationUpdate? update)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (update is null)
            return BadRequest(new { error = "Request body is required." });

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

        if (!LocationUpdateValidation.TryValidate(storedUpdate, out var validatedUpdate, out var errors))
            return BadRequest(new { error = "Invalid location update.", details = errors });

        store.AddPosition(validatedUpdate);
        logger.LogInformation("[{Session}] {Runner} → {Lat:F6}, {Lon:F6}  heading={Heading}  t={Timestamp:HH:mm:ss}",
            validatedUpdate.SessionCode, validatedUpdate.RunnerName,
            validatedUpdate.Latitude, validatedUpdate.Longitude,
            validatedUpdate.Heading.HasValue ? $"{validatedUpdate.Heading:F1}°" : "n/a",
            validatedUpdate.Timestamp);
        var participants = store.GetParticipants(validatedUpdate.SessionCode);
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
