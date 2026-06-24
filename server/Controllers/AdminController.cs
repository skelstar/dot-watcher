using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class AdminController(SessionStore store, BearerTokenAuth auth) : ControllerBase
{
    [HttpGet("/admin/users")]
    public IActionResult GetUsers()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAllUsers());
    }

    [HttpDelete("/admin/users/{userId}")]
    public IActionResult DeleteUser(string userId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return store.DeleteUserAccount(userId) ? NoContent() : NotFound();
    }

    [HttpGet("/admin/sessions")]
    public IActionResult GetSessions()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAllSessions());
    }

    [HttpDelete("/admin/sessions/{sessionId}")]
    public IActionResult DeleteSession(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        return store.DeleteSession(sessionId) ? NoContent() : NotFound();
    }

    [HttpGet("/admin/join-requests")]
    public IActionResult GetJoinRequests()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAllJoinRequests());
    }

    [HttpGet("/admin/sessions/{sessionId}/records")]
    public IActionResult GetSessionRecords(string sessionId, [FromQuery] int limit = 20)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var records = store.GetRecentLocationUpdates(sessionId, Math.Min(limit, 100));
        return Ok(records);
    }

    [HttpGet("/admin/sessions/{sessionId}/member-stats")]
    public IActionResult GetSessionMemberStats(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetSessionMemberStats(sessionId));
    }

    [HttpDelete("/admin/join-requests/{requestId}")]
    public IActionResult DeleteJoinRequest(string requestId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return store.DeleteJoinRequest(requestId) ? NoContent() : NotFound();
    }
}
