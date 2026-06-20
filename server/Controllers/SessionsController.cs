using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class SessionsController(
    SessionStore store,
    BearerTokenAuth auth,
    ILogger<SessionsController> logger) : ControllerBase
{
    [HttpGet("/sessions")]
    public IActionResult GetSessions() =>
        Ok(store.GetRecordedSessions());

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
