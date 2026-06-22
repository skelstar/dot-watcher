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

    [HttpDelete("/admin/sessions/{sessionCode}")]
    public IActionResult DeleteSession(string sessionCode)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var code = SessionStore.NormalizeSessionCode(sessionCode);
        if (code is null)
            return BadRequest(new { error = "Invalid session code." });

        return store.DeleteSession(code) ? NoContent() : NotFound();
    }

    [HttpGet("/admin/join-requests")]
    public IActionResult GetJoinRequests()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAllJoinRequests());
    }
}
