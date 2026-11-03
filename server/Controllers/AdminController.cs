using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class AdminController(SessionStore store, BearerTokenAuth auth, FeatureFlags features, PaletteProvider palette) : ControllerBase
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

    /// <summary>Admin/ops: replaces the features granted to an account (e.g. "crew"). An empty list revokes all.</summary>
    [HttpPut("/admin/users/{userId}/features")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SetUserFeatures(string userId, [FromBody] SetFeaturesRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (request?.Features is null)
            return BadRequest(new { error = "features is required." });

        var features = FeatureFlags.Normalize(request.Features);
        if (features.Any(f => f.Contains(',')))
            return BadRequest(new { error = "Feature names can't contain commas." });

        return store.SetUserFeatures(userId, features) ? Ok(features) : NotFound();
    }

    /// <summary>Admin/ops: every saved palette, with the app versions each is assigned to.</summary>
    [HttpGet("/admin/palettes")]
    [ProducesResponseType(typeof(IReadOnlyList<PaletteEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetPalettes()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(palette.List());
    }

    /// <summary>Admin/ops: one saved palette.</summary>
    [HttpGet("/admin/palettes/{id}")]
    [ProducesResponseType(typeof(PaletteEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetPalette(string id)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return palette.Get(id) is { } entry ? Ok(entry) : NotFound();
    }

    /// <summary>Admin/ops: saves a new palette (a name is required). It reaches no phone until it is assigned to an app version.</summary>
    [HttpPost("/admin/palettes")]
    [ProducesResponseType(typeof(PaletteEntry), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult CreatePalette([FromBody] Palette? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (request is null)
            return BadRequest(new { error = "A palette is required." });

        var problem = palette.Create(request, out var entry);
        return problem is null ? Created($"/admin/palettes/{entry!.Id}", entry) : BadRequest(new { error = problem });
    }

    /// <summary>Admin/ops: replaces a saved palette. Phones it is assigned to get the change within a few seconds.</summary>
    [HttpPut("/admin/palettes/{id}")]
    [ProducesResponseType(typeof(PaletteEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UpdatePalette(string id, [FromBody] Palette? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (request is null)
            return BadRequest(new { error = "A palette is required." });

        var problem = palette.Update(id, request, out var entry);
        if (problem is not null)
            return BadRequest(new { error = problem });

        return entry is null ? NotFound() : Ok(entry);
    }

    /// <summary>Admin/ops: deletes a saved palette. Refused with 409 while it is assigned to an app version.</summary>
    [HttpDelete("/admin/palettes/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult DeletePalette(string id)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return palette.Delete(id) switch
        {
            PaletteDeleteResult.Deleted => NoContent(),
            PaletteDeleteResult.InUse => Conflict(new { error = "The palette is assigned to an app version. Unassign it first." }),
            _ => NotFound(),
        };
    }

    /// <summary>Admin/ops: which palette each app version gets ("fallback" is everyone else).</summary>
    [HttpGet("/admin/palettes/assignments")]
    [ProducesResponseType(typeof(IReadOnlyList<PaletteAssignment>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetPaletteAssignments()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return Ok(palette.Assignments());
    }

    /// <summary>
    /// Admin/ops: gives a saved palette to phones on one app version (for example <c>1.2.2</c>, matched to the part of
    /// <c>X-App-Version</c> before the build number), or to <c>fallback</c> for every other version. Takes effect
    /// within a few seconds, on each phone's next launch or return to the front.
    /// </summary>
    [HttpPut("/admin/palettes/assignments/{appVersion}")]
    [ProducesResponseType(typeof(PaletteAssignment), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult AssignPalette(string appVersion, [FromBody] AssignPaletteRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (!PaletteProvider.IsValidRoute(appVersion))
            return BadRequest(new { error = "The app version must be like 1.2.2, or \"fallback\"." });

        if (string.IsNullOrWhiteSpace(request?.PaletteId))
            return BadRequest(new { error = "paletteId is required." });

        return palette.Assign(appVersion, request.PaletteId) ? Ok(new PaletteAssignment(appVersion, request.PaletteId)) : NotFound();
    }

    /// <summary>Admin/ops: stops giving a palette to an app version, so those phones fall back to the fallback palette (or the built-in colours).</summary>
    [HttpDelete("/admin/palettes/assignments/{appVersion}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UnassignPalette(string appVersion)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        return palette.Unassign(appVersion) ? NoContent() : NotFound();
    }

    /// <summary>Admin/ops: the global kill switch for each feature. A feature is on unless switched off here.</summary>
    [HttpGet("/admin/features")]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureSwitch>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetFeatureSwitches()
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        var off = features.SwitchedOff();
        return Ok(Features.All.Select(f => new FeatureSwitch(f, !off.Contains(f))).ToList());
    }

    /// <summary>Admin/ops: switches a feature on or off for every account. Off makes its endpoints look absent (404).</summary>
    [HttpPut("/admin/features/{feature}")]
    [ProducesResponseType(typeof(FeatureSwitch), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SetFeatureSwitch(string feature, [FromBody] SetFeatureSwitchRequest? request)
    {
        if (!auth.IsAuthorized(Request))
            return Unauthorized();

        if (request?.Enabled is not bool enabled)
            return BadRequest(new { error = "enabled is required." });

        if (!Features.All.Contains(feature))
            return NotFound();

        features.SetSwitch(feature, enabled);
        return Ok(new FeatureSwitch(feature, enabled));
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
        return Ok(membership);
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
