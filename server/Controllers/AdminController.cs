using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class AdminController(SessionStore store, BearerTokenAuth auth) : ControllerBase
{
    /// <summary>Admin/ops: lists all registered user accounts.</summary>
    [HttpGet("/admin/users")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminUserSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetUsers()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAllUsers());
    }

    /// <summary>Admin/ops: deletes a user account.</summary>
    [HttpDelete("/admin/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteUser(string userId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return store.DeleteUserAccount(userId) ? NoContent() : NotFound();
    }

    /// <summary>Admin/ops: lists all sessions, including ones without a saved recording.</summary>
    [HttpGet("/admin/sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminSessionSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSessions()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAllSessions());
    }

    /// <summary>Admin/ops: deletes a session and its recording.</summary>
    [HttpDelete("/admin/sessions/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteSession(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        return store.DeleteSession(sessionId) ? NoContent() : NotFound();
    }

    /// <summary>Admin/ops: gets the most recent recorded location updates for a session (default 20, max 100).</summary>
    [HttpGet("/admin/sessions/{sessionId}/records")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminLocationRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSessionRecords(string sessionId, [FromQuery] int limit = 20)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var records = store.GetRecentLocationUpdates(sessionId, Math.Min(limit, 100));
        return Ok(records);
    }

    /// <summary>Admin/ops: gets per-member position counts and last-seen timestamps for a session.</summary>
    [HttpGet("/admin/sessions/{sessionId}/member-stats")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminMemberStats>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSessionMemberStats(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetSessionMemberStats(sessionId));
    }
}
