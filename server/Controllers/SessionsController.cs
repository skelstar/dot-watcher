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
            ? Conflict(new { error = "A session with that name already exists." })
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
        var membership = store.JoinSessionByInvite(
            inviteCode,
            user.UserId,
            displayName,
            role);

        return membership is null
            ? NotFound(new { error = "Invite not found." })
            : Ok(membership);
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

    [HttpGet("/sessions/browse")]
    public IActionResult BrowseSessions()
    {
        if (!userAuth.TryAuthenticate(Request, out _))
            return Unauthorized();

        return Ok(store.GetBrowsableSessions());
    }

    [HttpPost("/sessions/{sessionId}/join-requests")]
    public IActionResult CreateJoinRequest(string sessionId, [FromBody] CreateJoinRequestRequest? request)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var displayName = string.IsNullOrWhiteSpace(request?.DisplayName)
            ? user.DisplayName
            : request.DisplayName.Trim();

        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        var role = request?.Role?.Trim().ToLowerInvariant() == "viewer" ? "viewer" : "runner";
        var result = store.CreateJoinRequest(sessionId, user.UserId, displayName, role);
        return result.Status switch
        {
            CreateJoinRequestStatus.Created => Ok(result.Request),
            CreateJoinRequestStatus.AlreadyPending => Ok(result.Request),
            CreateJoinRequestStatus.AlreadyMember => Conflict(new { error = "Already a member of this session." }),
            CreateJoinRequestStatus.SessionNotFound => NotFound(new { error = "Session not found." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [HttpGet("/sessions/{sessionId}/join-requests")]
    public IActionResult GetJoinRequests(string sessionId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (!store.CanReadSession(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        var requests = store.GetJoinRequests(sessionId);
        return requests is null
            ? NotFound(new { error = "Session not found." })
            : Ok(requests);
    }

    [HttpPost("/sessions/{sessionId}/join-requests/{requestId}/approve")]
    public IActionResult ApproveJoinRequest(string sessionId, string requestId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (!store.CanReadSession(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        var membership = store.ApproveJoinRequest(requestId, sessionId);
        return membership is null
            ? NotFound(new { error = "Join request not found." })
            : Ok(membership);
    }

    [HttpPost("/sessions/{sessionId}/join-requests/{requestId}/deny")]
    public IActionResult DenyJoinRequest(string sessionId, string requestId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        if (!store.CanReadSession(sessionId, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return store.DenyJoinRequest(requestId, sessionId)
            ? NoContent()
            : NotFound(new { error = "Join request not found." });
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
    public IActionResult DownloadRecording(string sessionId)
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

        var ndjson = store.GetRecordingAsNdjson(sessionId);
        return Content(ndjson, "application/x-ndjson");
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

}
