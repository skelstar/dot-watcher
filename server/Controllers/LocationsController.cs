using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LocationsController(
    SessionStore store,
    BearerTokenAuth auth,
    ILogger<LocationsController> logger) : ControllerBase
{
    [HttpPost("/location")]
    public IActionResult AddLocation([FromBody] LocationUpdate update)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        store.AddPosition(update);
        logger.LogInformation("[{Session}] {Runner} → {Lat:F6}, {Lon:F6}  heading={Heading}  t={Timestamp:HH:mm:ss}",
            update.SessionCode, update.RunnerName,
            update.Latitude, update.Longitude,
            update.Heading.HasValue ? $"{update.Heading:F1}°" : "n/a",
            update.Timestamp);
        var participants = store.GetParticipants(update.SessionCode);
        return Ok(new { participants });
    }

    [HttpGet("/locations/{sessionCode}")]
    public IActionResult GetLatestPositions(string sessionCode) =>
        Ok(store.GetLatestPositions(sessionCode));
}
