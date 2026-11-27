using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// The admin page's Groups tab (see .ai/plans/go-and-groups.md): list every group and change it without
/// being its organiser: name, length, nearby area, entry code, members, ending and reopening. Deleting a
/// group is the Sessions tab's DELETE /admin/sessions/{id}.
/// </summary>
[ApiController]
public class AdminGroupsController(SessionStore store, BearerTokenAuth auth) : ControllerBase
{
    /// <summary>The most hours a group can last, as when creating one.</summary>
    public const int MaxLengthHours = 240;

    [HttpGet("/admin/groups")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminGroupSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetGroups()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(store.GetAdminGroups(DateTimeOffset.UtcNow));
    }

    [HttpGet("/admin/groups/{sessionId}/members")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminGroupMember>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetMembers(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (store.GetAdminGroup(sessionId, DateTimeOffset.UtcNow) is null)
            return NotFound();

        return Ok(store.GetAdminGroupMembers(sessionId));
    }

    /// <summary>Changes the fields given and returns the group as it is now.</summary>
    [HttpPatch("/admin/groups/{sessionId}")]
    [ProducesResponseType(typeof(AdminGroupSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UpdateGroup(string sessionId, [FromBody] AdminUpdateGroupRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (request is null)
            return BadRequest(new { error = "Nothing to change." });

        string? name = null;
        if (request.SessionName is not null && (name = NearbyJoin.NormalizeTitle(request.SessionName)) is null)
            return BadRequest(new { error = $"A group's name must be 1-{NearbyJoin.MaxTitleLength} characters." });

        if (request.LengthHours is int length && length is < 1 or > MaxLengthHours)
            return BadRequest(new { error = $"A group lasts 1-{MaxLengthHours} hours from when it was created." });

        if (request.NearbyRadiusMeters is int radius && !NearbyJoin.IsAllowedRadius(radius))
            return BadRequest(new { error = "The join area must be 50, 100, 250 or 500 metres." });

        if ((request.JoinLatitude is null) != (request.JoinLongitude is null)
            || (request.JoinLatitude is not null && !NearbyJoin.IsValidPosition(request.JoinLatitude, request.JoinLongitude, null)))
            return BadRequest(new { error = "A join location needs a valid latitude and longitude." });

        string? code = null;
        var clearCode = request.EntryCode is not null && request.EntryCode.Trim().Length == 0;
        if (request.EntryCode is not null && !clearCode && (code = EntryCode.Normalize(request.EntryCode)) is null)
            return BadRequest(new { error = $"An entry code is {EntryCode.MinLength}-{EntryCode.MaxLength} letters or numbers." });

        var changes = new AdminGroupChanges(name, request.LengthHours, request.Nearby, request.NearbyRadiusMeters,
            request.JoinLatitude, request.JoinLongitude, code, clearCode);
        return store.UpdateAdminGroup(sessionId, changes) switch
        {
            AdminGroupUpdateOutcome.NotFound => NotFound(),
            AdminGroupUpdateOutcome.NeedsJoinLocation =>
                BadRequest(new { error = "Set a join location before showing the group to people nearby." }),
            _ => Ok(store.GetAdminGroup(sessionId, DateTimeOffset.UtcNow)),
        };
    }

    /// <summary>Ends the group for everyone, as its organiser's End group does.</summary>
    [HttpPost("/admin/groups/{sessionId}/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult EndGroup(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return store.AdminEndGroup(sessionId) ? NoContent() : NotFound();
    }

    /// <summary>Undoes an end, so people can join again. Members who left stay out.</summary>
    [HttpPost("/admin/groups/{sessionId}/reopen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult ReopenGroup(string sessionId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return store.AdminReopenGroup(sessionId) ? NoContent() : NotFound();
    }

    /// <summary>Puts an account in the group, with no distance or entry code check.</summary>
    [HttpPost("/admin/groups/{sessionId}/members")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult AddMember(string sessionId, [FromBody] AdminAddGroupMemberRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var username = request?.Username?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(username))
            return BadRequest(new { error = "Give a username." });

        var displayName = string.IsNullOrWhiteSpace(request!.DisplayName) ? null : request.DisplayName.Trim();
        return store.AdminAddGroupMember(sessionId, username, displayName) switch
        {
            AdminAddMemberOutcome.NoGroup => NotFound(new { error = "No such group." }),
            AdminAddMemberOutcome.NoUser => NotFound(new { error = $"No account called {username}." }),
            AdminAddMemberOutcome.Ended => Conflict(new { error = "The group has ended. Reopen it first." }),
            _ => NoContent(),
        };
    }

    /// <summary>Takes someone out of the group, as if they'd left. A group with no runners left ends.</summary>
    [HttpDelete("/admin/groups/{sessionId}/members/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RemoveMember(string sessionId, string userId)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (store.GetAdminGroup(sessionId, DateTimeOffset.UtcNow) is null)
            return NotFound();

        return store.LeaveSession(sessionId, userId) ? NoContent() : NotFound();
    }
}
