using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LogsController(LogBuffer log, BearerTokenAuth auth) : ControllerBase
{
    [HttpGet("/log")]
    public IActionResult GetLog() =>
        Ok(log.GetAll());

    [HttpDelete("/log")]
    public IActionResult ClearLog()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        log.Clear();
        return NoContent();
    }
}
