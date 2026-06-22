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

        if (request.SessionCode is not null && SessionStore.NormalizeSessionCode(request.SessionCode) is null)
            return BadRequest(new { error = "Session code must be 3-32 letters, numbers, dashes, or underscores." });

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? user.DisplayName
            : request.DisplayName.Trim();

        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        try
        {
            return Ok(store.CreateSessionForUser(user.UserId, displayName, request.SessionCode));
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { error = "Session code is already in use." });
        }
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

        var membership = store.JoinSessionByInvite(
            inviteCode,
            user.UserId,
            displayName);

        return membership is null
            ? NotFound(new { error = "Invite not found." })
            : Ok(membership);
    }

    [HttpGet("/sessions/{sessionCode}/members")]
    public IActionResult GetSessionMembers(string sessionCode)
    {
        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!TryAuthorizeSessionOwnerOrAdmin(code, out var error))
            return error!;

        var members = store.GetSessionMembers(code);
        return members is null
            ? NotFound(new { error = "Session not found." })
            : Ok(members);
    }

    [HttpPost("/sessions/{sessionCode}/members/{userId}/role")]
    public IActionResult UpdateSessionMemberRole(
        string sessionCode,
        string userId,
        [FromBody] UpdateSessionMemberRoleRequest? request)
    {
        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        var role = request?.Role?.Trim().ToLowerInvariant();
        if (role is not ("runner" or "viewer"))
            return BadRequest(new { error = "Role must be runner or viewer." });

        if (!TryAuthorizeSessionOwnerOrAdmin(code, out var error))
            return error!;

        var result = store.UpdateSessionMemberRole(code, userId, role);
        return result.Status switch
        {
            UpdateSessionMemberRoleStatus.Updated => Ok(result.Member),
            UpdateSessionMemberRoleStatus.SessionNotFound => NotFound(new { error = "Session not found." }),
            UpdateSessionMemberRoleStatus.MemberNotFound => NotFound(new { error = "Member not found." }),
            UpdateSessionMemberRoleStatus.OwnerRoleImmutable => BadRequest(new { error = "Owner role cannot be changed." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [HttpGet("/sessions/browse")]
    public IActionResult BrowseSessions()
    {
        if (!userAuth.TryAuthenticate(Request, out _))
            return Unauthorized();

        return Ok(store.GetBrowsableSessions());
    }

    [HttpPost("/sessions/{sessionCode}/join-requests")]
    public IActionResult CreateJoinRequest(string sessionCode, [FromBody] CreateJoinRequestRequest? request)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        var displayName = string.IsNullOrWhiteSpace(request?.DisplayName)
            ? user.DisplayName
            : request.DisplayName.Trim();

        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        var result = store.CreateJoinRequest(code, user.UserId, displayName);
        return result.Status switch
        {
            CreateJoinRequestStatus.Created => Ok(result.Request),
            CreateJoinRequestStatus.AlreadyPending => Ok(result.Request),
            CreateJoinRequestStatus.AlreadyMember => Conflict(new { error = "Already a member of this session." }),
            CreateJoinRequestStatus.OwnSession => BadRequest(new { error = "Cannot request to join your own session." }),
            CreateJoinRequestStatus.SessionNotFound => NotFound(new { error = "Session not found." }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [HttpGet("/sessions/{sessionCode}/join-requests")]
    public IActionResult GetJoinRequests(string sessionCode)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.IsSessionOwner(code, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        var requests = store.GetJoinRequests(code);
        return requests is null
            ? NotFound(new { error = "Session not found." })
            : Ok(requests);
    }

    [HttpPost("/sessions/{sessionCode}/join-requests/{requestId}/approve")]
    public IActionResult ApproveJoinRequest(string sessionCode, string requestId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.IsSessionOwner(code, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        var membership = store.ApproveJoinRequest(requestId, code);
        return membership is null
            ? NotFound(new { error = "Join request not found." })
            : Ok(membership);
    }

    [HttpPost("/sessions/{sessionCode}/join-requests/{requestId}/deny")]
    public IActionResult DenyJoinRequest(string sessionCode, string requestId)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.IsSessionOwner(code, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return store.DenyJoinRequest(requestId, code)
            ? NoContent()
            : NotFound(new { error = "Join request not found." });
    }

    [HttpDelete("/me/sessions/{sessionCode}")]
    public IActionResult DeleteMySession(string sessionCode)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.IsSessionOwner(code, user.UserId))
            return StatusCode(StatusCodes.Status403Forbidden);

        return store.DeleteSession(code) ? NoContent() : NotFound();
    }

    [HttpPost("/sessions/{sessionCode}/recording")]
    public async Task<IActionResult> UploadRecording(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        using var reader = new StreamReader(Request.Body);
        var content = await reader.ReadToEndAsync();

        try
        {
            store.SaveRecording(code, content);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid NDJSON recording." });
        }
        catch (LocationUpdateValidationException ex)
        {
            return BadRequest(new { error = "Invalid NDJSON recording.", details = ex.Errors });
        }

        logger.LogInformation("Uploaded recording for {Session} ({Bytes} bytes)", code, content.Length);
        return Ok(new { sessionCode = code });
    }

    [HttpGet("/sessions/{sessionCode}/recording")]
    public IActionResult DownloadRecording(string sessionCode)
    {
        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (!auth.IsAuthorized(Request))
        {
            if (!userAuth.TryAuthenticate(Request, out var user))
                return Unauthorized();

            if (code is null)
                return BadRequest(new { error = "Invalid session code." });

            if (!store.CanReadSession(code, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden);
        }
        else if (code is null)
        {
            return BadRequest(new { error = "Invalid session code." });
        }

        if (!store.HasRecording(code))
            return NotFound();

        var ndjson = store.GetRecordingAsNdjson(code);
        return Content(ndjson, "application/x-ndjson");
    }

    [HttpDelete("/sessions/{sessionCode}/recording")]
    public IActionResult DeleteRecording(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.DeleteRecording(code))
            return NotFound();

        logger.LogInformation("Deleted recording for {Session}", code);
        return NoContent();
    }

    [HttpDelete("/sessions/{sessionCode}")]
    public IActionResult ClearSession(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        store.ClearSession(code);
        return NoContent();
    }

    [HttpPost("/sessions/{targetCode}/merge-from/{sourceCode}")]
    public IActionResult MergeSession(string targetCode, string sourceCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var tgt = SessionStore.NormalizeSessionCode(targetCode);
        var src = SessionStore.NormalizeSessionCode(sourceCode);
        if (tgt is null || src is null)
            return BadRequest(new { error = "Invalid session code." });

        if (!store.HasRecording(src))
            return NotFound(new { error = $"Source session '{src}' not found" });

        var rows = store.MergeSession(src, tgt);
        logger.LogInformation("Merged session {Source} into {Target} ({Rows} records)", src, tgt, rows);
        return Ok(new { sourceCode = src, targetCode = tgt, recordsMerged = rows });
    }

    private bool TryAuthorizeSessionOwnerOrAdmin(string sessionCode, out IActionResult? error)
    {
        error = null;
        if (auth.IsAuthorized(Request))
            return true;

        if (!userAuth.TryAuthenticate(Request, out var user))
        {
            error = Unauthorized();
            return false;
        }

        if (!store.IsSessionOwner(sessionCode, user.UserId))
        {
            error = StatusCode(StatusCodes.Status403Forbidden);
            return false;
        }

        return true;
    }
}
