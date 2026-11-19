using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

public record AppActivityRequest(string? Kind);

/// <summary>
/// Activity the app reports itself, because no other request says it happened: coming to the front
/// (<c>app_opened</c>) and going to the background (<c>app_backgrounded</c>). The kinds allowed are the
/// register's recording entries for this route. See .ai/plans/activity-flow.md.
/// </summary>
[ApiController]
public class AppActivityController(UserTokenAuth userAuth, TimeProvider timeProvider) : ControllerBase
{
    // A repeat of the same kind from the same device within this long is dropped: flicking between
    // apps, or the double foreground some Android dialogs cause.
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(60);
    private const int MaxRemembered = 20_000;

    // user|device -> the last kind reported and when. Only a repeat of that same kind is dropped, so
    // away-and-back-again inside a minute still records both.
    private static readonly ConcurrentDictionary<string, (string Kind, DateTimeOffset At)> _lastReported = new();

    /// <summary>
    /// Records that the app came to the front or went to the background, for the admin Activity tab.
    /// 204 whether or not it was recorded (a quick repeat isn't); 400 for a kind the app can't send.
    /// </summary>
    [HttpPost("/me/activity")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Report([FromBody] AppActivityRequest? request)
    {
        if (!userAuth.TryAuthenticate(Request, out var user))
            return Unauthorized();

        var allowed = ActivityRegister.KindsFor("POST", "/me/activity");
        if (request?.Kind is not { } kind || !allowed.Contains(kind))
            return BadRequest(new { error = $"kind must be one of: {string.Join(", ", allowed)}." });

        var device = Request.Headers.TryGetValue("X-Device-Id", out var deviceId) ? deviceId.ToString() : "";
        var key = $"{user.UserId}|{device}";
        var now = timeProvider.GetUtcNow();
        if (_lastReported.TryGetValue(key, out var last) && last.Kind == kind && now - last.At < RepeatWindow)
        {
            HttpContext.SkipActivity();
            return NoContent();
        }

        if (_lastReported.Count > MaxRemembered)
            _lastReported.Clear();
        _lastReported[key] = (kind, now);
        HttpContext.SetActivity(kind: kind);
        return NoContent();
    }
}
