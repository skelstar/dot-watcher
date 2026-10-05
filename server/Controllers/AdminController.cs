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

    /// <summary>Admin/ops: exports every stored location update for a session as NDJSON, with no
    /// run-gap or row-count truncation applied - for diagnosing replay truncation issues.</summary>
    [HttpGet("/admin/sessions/{sessionId}/records/export")]
    [Produces("application/x-ndjson")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult ExportSessionRecords(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var ndjson = store.GetAllRecordingRowsAsNdjson(sessionId);
        return Content(ndjson, "application/x-ndjson");
    }

    /// <summary>Admin/ops: creates a session owned by an existing account (looked up by username),
    /// for callers such as the admin panel's NDJSON import that have no session-owning user token
    /// of their own.</summary>
    [HttpPost("/admin/sessions")]
    [ProducesResponseType(typeof(SessionMembership), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult CreateSessionForOwner([FromBody] AdminCreateSessionRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request?.OwnerUsername))
            return BadRequest(new { error = "ownerUsername is required." });

        if (request.SessionName is not null && SessionStore.NormalizeSessionName(request.SessionName) is null)
            return BadRequest(new { error = "Session name must be 4-8 letters, numbers, dashes, or underscores." });

        var owner = store.GetUserByUsername(request.OwnerUsername);
        if (owner is null)
            return NotFound(new { error = "Owner not found." });

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? owner.DisplayName : request.DisplayName.Trim();
        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        var membership = store.CreateSessionForUser(owner.Id, displayName, request.SessionName);
        return membership is null
            ? Conflict(new { error = "A session with that name already exists. Try entering the invite code." })
            : Ok(membership);
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
