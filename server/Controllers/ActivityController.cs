using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>Which of a dot's activity to mark: a Go run, or a stretch of time. <c>isTest</c> defaults to true.</summary>
public record MarkActivityTestRequest(string? UserId, string? RunId, DateTimeOffset? From, DateTimeOffset? To, bool? IsTest);

public record MarkActivityTestResponse(int Changed);

/// <summary>
/// Admin/ops: what each dot did, for the admin Activity tab's flow. Times are instants; the admin page
/// works out the day in its own time zone and sends its start and end. See .ai/plans/activity-flow.md.
/// </summary>
[ApiController]
public class ActivityController(SessionStore store, BearerTokenAuth auth) : ControllerBase
{
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    /// <summary>Admin/ops: everyone with activity between <c>from</c> and <c>to</c>, busiest first. Events marked as test count only with <c>includeTest</c>.</summary>
    [HttpGet("/admin/activity/dots")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityDot>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetDots([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] bool includeTest = false)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();
        if (InvalidRange(from, to) is { } bad)
            return bad;

        return Ok(store.GetActivityDots(from!.Value, to!.Value, includeTest));
    }

    /// <summary>Admin/ops: the events of the dots in <c>userIds</c> (comma separated) between <c>from</c> and <c>to</c>, oldest first. Events marked as test only with <c>includeTest</c>.</summary>
    [HttpGet("/admin/activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityEventView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetEvents(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? userIds, [FromQuery] bool includeTest = false)
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

        return Ok(store.GetActivityEvents(from!.Value, to!.Value, ids, includeTest));
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

    /// <summary>
    /// Admin/ops: marks one dot's activity as test (testing on the live server), or back to real with
    /// <c>isTest: false</c>. Either a Go run (<c>runId</c>: everything from its first event to its last)
    /// or a stretch (<c>from</c>, <c>to</c>, at most 7 days). Nothing is deleted. Returns how many events changed.
    /// </summary>
    [HttpPost("/admin/activity/test")]
    [ProducesResponseType(typeof(MarkActivityTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult MarkTest([FromBody] MarkActivityTestRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request?.UserId))
            return BadRequest(new { error = "userId is required." });

        var hasRun = !string.IsNullOrWhiteSpace(request.RunId);
        if (hasRun == (request.From is not null || request.To is not null))
            return BadRequest(new { error = "Give either runId, or from and to." });
        if (!hasRun && InvalidRange(request.From, request.To) is { } bad)
            return bad;

        var changed = store.MarkActivityTest(
            request.UserId, request.IsTest ?? true, hasRun ? request.RunId : null, request.From, request.To);
        return Ok(new MarkActivityTestResponse(changed));
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
