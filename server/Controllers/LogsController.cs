using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class LogsController(LogBuffer log, BearerTokenAuth auth) : ControllerBase
{
    /// <summary>Admin/ops: gets the in-memory buffer of recent application log lines.</summary>
    [HttpGet("/log")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetLog()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(log.GetAll());
    }

    /// <summary>Admin/ops: clears the in-memory application log buffer.</summary>
    [HttpDelete("/log")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult ClearLog()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        log.Clear();
        return NoContent();
    }
}
