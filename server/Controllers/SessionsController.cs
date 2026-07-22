using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class SessionsController(
    SessionStore store,
    BearerTokenAuth auth,
    UserTokenAuth userAuth,
    ILogger<SessionsController> logger) : ControllerBase
{
    /// <summary>Lists the caller's active session memberships.</summary>
    [HttpGet("/me/sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionMembership>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetMySessions()
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return Ok(store.GetSessionsForUser(user.UserId));
    }

    /// <summary>Lists sessions the caller recently left, most recent first.</summary>
    [HttpGet("/me/sessions/recent")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionMembership>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetMyRecentSessions()
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return Ok(store.GetRecentLeftSessions(user.UserId));
    }

    /// <summary>Admin/ops: lists IDs of all sessions with a saved recording, newest first.</summary>
    [HttpGet("/sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSessions()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetRecordedSessions());
    }

    /// <summary>Creates a session and returns the caller's runner membership for it.</summary>
    [HttpPost("/sessions")]
    [ProducesResponseType(typeof(SessionMembership), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult CreateSession([FromBody] CreateSessionRequest? request)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        request ??= new CreateSessionRequest();

        if (request.SessionName is not null && SessionStore.NormalizeSessionName(request.SessionName) is null)
            return BadRequest(new { error = "Session name must be 4-8 letters, numbers, dashes, or underscores." });

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? user.DisplayName
            : request.DisplayName.Trim();

        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        var membership = store.CreateSessionForUser(user.UserId, displayName, request.SessionName);
        return membership is null
            ? Conflict(new { error = "A session with that name already exists. Try entering the invite code." })
            : Ok(membership);
    }

    /// <summary>
    /// Joins a session by invite code and returns the membership, plus a Participants array
    /// (session-joined runners, not just currently-tracking ones) as an additive field.
    /// </summary>
    [HttpPost("/session-invites/{inviteCode}/join")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult JoinSession(string inviteCode, [FromBody] JoinSessionRequest? request)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        request ??= new JoinSessionRequest();

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? user.DisplayName
            : request.DisplayName.Trim();

        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        var role = request?.Role?.Trim().ToLowerInvariant() == "runner" ? "runner" : "viewer";
        var (membership, archived) = store.JoinSessionByInvite(
            inviteCode,
            user.UserId,
            displayName,
            role);

        if (archived)
            return StatusCode(StatusCodes.Status410Gone, new { error = "This session has ended and can no longer be joined." });

        if (membership is null)
            return NotFound(new { error = "Invite not found." });

        // Additive field alongside the existing SessionMembership shape (not a replacement) so
        // older clients that decode a fixed SessionMembership struct are unaffected. Uses the
        // joined-runners roster (GetSessionRunners), not GetParticipants (who's actively
        // posting) — the point is for a new joiner to immediately see everyone already in the
        // session, even ones who haven't started tracking yet.
        var participants = store.GetSessionRunners(membership.SessionId);
        return Ok(new
        {
            membership.SessionId,
            membership.SessionName,
            membership.InviteCode,
            membership.Role,
            membership.DisplayName,
            membership.OwnerDisplayName,
            Participants = participants,
        });
    }

    /// <summary>Gets basic public info (session name, creator) for a session, by invite code. No auth required.</summary>
    [HttpGet("/session-invites/{inviteCode}")]
    [ProducesResponseType(typeof(SessionInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetSessionInfoByInviteCode(string inviteCode)
    {
        var info = store.GetSessionInfoByInviteCode(inviteCode);
        return info is null ? NotFound(new { error = "Invite not found." }) : Ok(info);
    }

    /// <summary>Gets the latest live position per runner for a session, by invite code. No auth required.</summary>
    [HttpGet("/session-invites/{inviteCode}/locations")]
    [ProducesResponseType(typeof(IReadOnlyList<RunnerPosition[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLocationsByInviteCode(string inviteCode)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        return Ok(store.GetLatestPositions(sessionId));
    }

    /// <summary>Downloads the session's recording as NDJSON (application/x-ndjson), by invite code. No auth required.</summary>
    [HttpGet("/session-invites/{inviteCode}/recording")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetRecordingByInviteCode(string inviteCode, DateTimeOffset? since, DateTimeOffset? until)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        if (!store.HasRecording(sessionId))
            return NotFound();

        return RecordingResult(sessionId, since, until);
    }

    /// <summary>Gets recording start/latest timestamps for a session, by invite code. No auth required.</summary>
    [HttpGet("/session-invites/{inviteCode}/recording/meta")]
    [ProducesResponseType(typeof(RecordingMeta), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetRecordingMetaByInviteCode(string inviteCode)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        var meta = store.GetRecordingMeta(sessionId);
        return meta is null ? NotFound() : Ok(meta);
    }

    /// <summary>Downloads the session's GPX route (application/gpx+xml), by invite code. No auth required.</summary>
    [HttpGet("/session-invites/{inviteCode}/route")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetRouteByInviteCode(string inviteCode)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        var gpx = store.GetRoute(sessionId);
        return gpx is null ? NotFound() : Content(gpx, "application/gpx+xml");
    }

    /// <summary>Lists display names of runners who have joined the session (not just those actively tracking).</summary>
    [HttpGet("/sessions/{sessionId}/runners")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetSessionRunners(string sessionId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (!store.CanReadSession(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return Ok(store.GetSessionRunners(sessionId));
    }

    /// <summary>Removes the caller's own membership from a session.</summary>
    [HttpDelete("/me/sessions/{sessionId}/membership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult LeaveSession(string sessionId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return store.LeaveSession(sessionId, user.UserId) ? NoContent() : NotFound();
    }

    /// <summary>Admin/ops: replaces a session's saved recording with an uploaded NDJSON body (any Content-Type; the body is read raw).</summary>
    [HttpPost("/sessions/{sessionId}/recording")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadRecording(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        using var reader = new StreamReader(Request.Body);
        var content = await reader.ReadToEndAsync();

        try
        {
            store.SaveRecording(sessionId, content);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid NDJSON recording." });
        }
        catch (LocationUpdateValidationException ex)
        {
            return BadRequest(new { error = "Invalid NDJSON recording.", details = ex.Errors });
        }

        logger.LogInformation("Uploaded recording for {Session} ({Bytes} bytes)", sessionId, content.Length);
        return Ok(new { sessionId = sessionId });
    }

    /// <summary>Downloads a session's recording as NDJSON (application/x-ndjson). Accepts either the admin bearer token or a member's user token.</summary>
    [HttpGet("/sessions/{sessionId}/recording")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DownloadRecording(string sessionId, DateTimeOffset? since, DateTimeOffset? until)
    {
        if (!auth.IsAuthorized(Request))
        {
            if (!userAuth.TryAuthenticate(Request, out var user))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { error = "Invalid session ID." });

            if (!store.CanReadSession(sessionId, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden);
        }
        else if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "Invalid session ID." });
        }

        if (!store.HasRecording(sessionId))
            return NotFound();

        return RecordingResult(sessionId, since, until);
    }

    /// <summary>Gets a session's recording start/latest timestamps. Accepts either the admin bearer token or a member's user token.</summary>
    [HttpGet("/sessions/{sessionId}/recording/meta")]
    [ProducesResponseType(typeof(RecordingMeta), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetRecordingMeta(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
        {
            if (!userAuth.TryAuthenticate(Request, out var user))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { error = "Invalid session ID." });

            if (!store.CanReadSession(sessionId, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden);
        }
        else if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "Invalid session ID." });
        }

        var meta = store.GetRecordingMeta(sessionId);
        return meta is null ? NotFound() : Ok(meta);
    }

    /// <summary>Admin/ops: deletes a session's saved recording.</summary>
    [HttpDelete("/sessions/{sessionId}/recording")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteRecording(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        if (!store.DeleteRecording(sessionId))
            return NotFound();

        logger.LogInformation("Deleted recording for {Session}", sessionId);
        return NoContent();
    }

    /// <summary>Replaces a session's GPX route with an uploaded body (raw XML text). Accepts either the admin bearer token or the session owner's user token.</summary>
    [HttpPost("/sessions/{sessionId}/route")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadRoute(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
        {
            if (!userAuth.TryAuthenticate(Request, out var user))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { error = "Invalid session ID." });

            if (!store.CanManageRoute(sessionId, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden);
        }
        else if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "Invalid session ID." });
        }

        using var reader = new StreamReader(Request.Body);
        var content = await reader.ReadToEndAsync();

        try
        {
            store.SaveRoute(sessionId, content);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Invalid GPX route." });
        }

        logger.LogInformation("Uploaded route for {Session} ({Bytes} bytes)", sessionId, content.Length);
        return Ok(new { sessionId = sessionId });
    }

    /// <summary>Downloads a session's GPX route (application/gpx+xml). Accepts either the admin bearer token or a member's user token.</summary>
    [HttpGet("/sessions/{sessionId}/route")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DownloadRoute(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
        {
            if (!userAuth.TryAuthenticate(Request, out var user))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { error = "Invalid session ID." });

            if (!store.CanReadSession(sessionId, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden);
        }
        else if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { error = "Invalid session ID." });
        }

        var gpx = store.GetRoute(sessionId);
        return gpx is null ? NotFound() : Content(gpx, "application/gpx+xml");
    }

    /// <summary>Admin/ops: deletes a session's saved GPX route.</summary>
    [HttpDelete("/sessions/{sessionId}/route")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteRoute(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        if (!store.DeleteRoute(sessionId))
            return NotFound();

        logger.LogInformation("Deleted route for {Session}", sessionId);
        return NoContent();
    }

    /// <summary>Admin/ops: clears live positions for a session. The saved recording is untouched.</summary>
    [HttpDelete("/sessions/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult ClearSession(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        store.ClearSession(sessionId);
        return NoContent();
    }

    /// <summary>Admin/ops: merges a source session's recorded records into a target session, then removes the source recording.</summary>
    [HttpPost("/sessions/{targetId}/merge-from/{sourceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult MergeSession(string targetId, string sourceId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(sourceId))
            return BadRequest(new { error = "Invalid session ID." });

        if (!store.HasRecording(sourceId))
            return NotFound(new { error = $"Source session '{sourceId}' not found" });

        var rows = store.MergeSession(sourceId, targetId);
        logger.LogInformation("Merged session {Source} into {Target} ({Rows} records)", sourceId, targetId, rows);
        return Ok(new { sourceId = sourceId, targetId = targetId, recordsMerged = rows });
    }

    // Omitting since/until preserves the original "whole latest run" behavior; passing either
    // switches to the windowed, run-clamped query used by the live scrubber.
    private IActionResult RecordingResult(string sessionId, DateTimeOffset? since, DateTimeOffset? until)
    {
        if (since is null && until is null)
        {
            var ndjson = store.GetRecordingAsNdjson(sessionId);
            return Content(ndjson, "application/x-ndjson");
        }

        var (windowNdjson, truncated) = store.GetRecordingWindowAsNdjson(sessionId, since, until);
        if (truncated)
            Response.Headers.Append("X-Truncated", "true");
        return Content(windowNdjson, "application/x-ndjson");
    }

}
