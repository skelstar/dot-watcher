using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// Admin/ops: what each dot did, for the admin Activity tab's flow. Times are instants; the admin page
/// works out the day in its own time zone and sends its start and end. See .ai/plans/activity-flow.md.
/// </summary>
[ApiController]
public class ActivityController(SessionStore store, BearerTokenAuth auth) : ControllerBase
{
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    /// <summary>Admin/ops: everyone with activity between <c>from</c> and <c>to</c>, busiest first.</summary>
    [HttpGet("/admin/activity/dots")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityDot>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetDots([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();
        if (InvalidRange(from, to) is { } bad)
            return bad;

        return Ok(store.GetActivityDots(from!.Value, to!.Value));
    }

    /// <summary>Admin/ops: the events of the dots in <c>userIds</c> (comma separated) between <c>from</c> and <c>to</c>, oldest first.</summary>
    [HttpGet("/admin/activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityEventView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetEvents([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? userIds)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();
        if (InvalidRange(from, to) is { } bad)
            return bad;

        var ids = (userIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();
        if (ids.Length is 0 or > 20)
            return BadRequest(new { error = "userIds needs 1 to 20 comma-separated ids." });

        return Ok(store.GetActivityEvents(from!.Value, to!.Value, ids));
    }

    /// <summary>Admin/ops: the activity register, every event recorded or asked for, with its label and where it shows.</summary>
    [HttpGet("/admin/activity/register")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityEventDefinition>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetRegister()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(ActivityRegister.Events);
    }

    private BadRequestObjectResult? InvalidRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is null || to is null)
            return BadRequest(new { error = "from and to are required." });
        if (to <= from || to - from > MaxRange)
            return BadRequest(new { error = "to must be after from, and at most 7 days later." });
        return null;
    }
}
