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
    [HttpGet("/me/sessions")]
    public IActionResult GetMySessions()
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return Ok(store.GetSessionsForUser(user.UserId));
    }

    [HttpGet("/me/sessions/recent")]
    public IActionResult GetMyRecentSessions()
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return Ok(store.GetRecentLeftSessions(user.UserId));
    }

    [HttpGet("/sessions")]
    public IActionResult GetSessions()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetRecordedSessions());
    }

    [HttpPost("/sessions")]
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

    [HttpPost("/session-invites/{inviteCode}/join")]
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
            Participants = participants,
        });
    }

    [HttpGet("/session-invites/{inviteCode}/locations")]
    public IActionResult GetLocationsByInviteCode(string inviteCode)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        return Ok(store.GetLatestPositions(sessionId));
    }

    [HttpGet("/session-invites/{inviteCode}/recording")]
    public IActionResult GetRecordingByInviteCode(string inviteCode, DateTimeOffset? since, DateTimeOffset? until)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        if (!store.HasRecording(sessionId))
            return NotFound();

        return RecordingResult(sessionId, since, until);
    }

    [HttpGet("/session-invites/{inviteCode}/recording/meta")]
    public IActionResult GetRecordingMetaByInviteCode(string inviteCode)
    {
        var sessionId = store.GetSessionIdByInviteCode(inviteCode);
        if (sessionId is null)
            return NotFound(new { error = "Invite not found." });

        var meta = store.GetRecordingMeta(sessionId);
        return meta is null ? NotFound() : Ok(meta);
    }

    [HttpGet("/sessions/{sessionId}/runners")]
    public IActionResult GetSessionRunners(string sessionId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (!store.CanReadSession(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return Ok(store.GetSessionRunners(sessionId));
    }

    [HttpDelete("/me/sessions/{sessionId}/membership")]
    public IActionResult LeaveSession(string sessionId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        return store.LeaveSession(sessionId, user.UserId) ? NoContent() : NotFound();
    }

    [HttpPost("/sessions/{sessionId}/recording")]
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

    [HttpGet("/sessions/{sessionId}/recording")]
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

    [HttpGet("/sessions/{sessionId}/recording/meta")]
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

    [HttpDelete("/sessions/{sessionId}/recording")]
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

    [HttpDelete("/sessions/{sessionId}")]
    public IActionResult ClearSession(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "Invalid session ID." });

        store.ClearSession(sessionId);
        return NoContent();
    }

    [HttpPost("/sessions/{targetId}/merge-from/{sourceId}")]
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
