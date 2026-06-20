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
            request.Role ?? "viewer",
            displayName);

        return membership is null
            ? NotFound(new { error = "Invite not found." })
            : Ok(membership);
    }

    [HttpPost("/sessions/{sessionCode}/recording")]
    public async Task<IActionResult> UploadRecording(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        using var reader = new StreamReader(Request.Body);
        var content = await reader.ReadToEndAsync();
        var upper = sessionCode.ToUpperInvariant();

        try
        {
            store.SaveRecording(upper, content);
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid NDJSON recording." });
        }

        logger.LogInformation("Uploaded recording for {Session} ({Bytes} bytes)", upper, content.Length);
        return Ok(new { sessionCode = upper });
    }

    [HttpGet("/sessions/{sessionCode}/recording")]
    public IActionResult DownloadRecording(string sessionCode)
    {
        var upper = sessionCode.ToUpperInvariant();
        if (!auth.IsAuthorized(Request))
        {
            if (!userAuth.TryAuthenticate(Request, out var user))
                return Unauthorized();

            var code = SessionStore.NormalizeSessionCode(sessionCode);
            if (code is null)
                return BadRequest(new { error = "Invalid session code." });

            if (!store.CanReadSession(code, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!store.HasRecording(upper))
            return NotFound();

        var ndjson = store.GetRecordingAsNdjson(upper);
        return Content(ndjson, "application/x-ndjson");
    }

    [HttpDelete("/sessions/{sessionCode}/recording")]
    public IActionResult DeleteRecording(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var upper = sessionCode.ToUpperInvariant();
        if (!store.DeleteRecording(upper))
            return NotFound();

        logger.LogInformation("Deleted recording for {Session}", upper);
        return NoContent();
    }

    [HttpDelete("/sessions/{sessionCode}")]
    public IActionResult ClearSession(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        store.ClearSession(sessionCode);
        return NoContent();
    }

    [HttpPost("/sessions/{targetCode}/merge-from/{sourceCode}")]
    public IActionResult MergeSession(string targetCode, string sourceCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var tgt = targetCode.ToUpperInvariant();
        var src = sourceCode.ToUpperInvariant();

        if (!store.HasRecording(src))
            return NotFound(new { error = $"Source session '{src}' not found" });

        var rows = store.MergeSession(src, tgt);
        logger.LogInformation("Merged session {Source} into {Target} ({Rows} records)", src, tgt, rows);
        return Ok(new { sourceCode = src, targetCode = tgt, recordsMerged = rows });
    }
}
