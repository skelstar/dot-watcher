using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

/// <summary>
/// A crew is the set of people you have chosen, mutual and tied to your account. People are added
/// only by redeeming a one-time code the other person sent them. See .ai/plans/crew-management.md.
/// </summary>
[ApiController]
public class CrewController(
    SessionStore store,
    UserTokenAuth userAuth,
    FeatureFlags features,
    CrewCodeLimiter limiter,
    CrewFinder finder,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaxNicknameLength = 30;

    /// <summary>Lists the caller's crew, most recently added first.</summary>
    [HttpGet("/me/crew")]
    [ProducesResponseType(typeof(CrewOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetCrew()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return Ok(store.GetCrewOverview(user.UserId));
    }

    /// <summary>Removes someone from the caller's crew, for both of them, immediately.</summary>
    [HttpDelete("/me/crew/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RemoveCrewMember(string userId)
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return store.RemoveCrewMember(user.UserId, userId) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Creates a one-time crew code (valid 24 hours) for the caller to send to one person. If the
    /// caller already has three live codes, the oldest is retired.
    /// </summary>
    [HttpPost("/me/crew/codes")]
    [ProducesResponseType(typeof(CrewCode), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult CreateCode()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return Ok(store.CreateCrewCode(user.UserId, timeProvider.GetUtcNow()));
    }

    /// <summary>Lists the caller's unused, unexpired crew codes, newest first.</summary>
    [HttpGet("/me/crew/codes")]
    [ProducesResponseType(typeof(IReadOnlyList<CrewCode>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetCodes()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return Ok(store.GetActiveCrewCodes(user.UserId, timeProvider.GetUtcNow()));
    }

    /// <summary>Revokes one of the caller's unused crew codes.</summary>
    [HttpDelete("/me/crew/codes/{code}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult RevokeCode(string code)
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return store.RevokeCrewCode(user.UserId, code) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Says whose code this is, for the "Add Priya to your crew?" confirmation. Adds no one. An
    /// unknown, expired, used or blocked code all return the same 404.
    /// </summary>
    [HttpGet("/me/crew/codes/{code}")]
    [ProducesResponseType(typeof(CrewCodePreview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult PreviewCode(string code)
    {
        if (Gate(out var user) is { } denied)
            return denied;

        if (!limiter.TryAccept(user.UserId))
            return TooManyAttempts();

        var (outcome, owner) = store.PreviewCrewCode(user.UserId, code, timeProvider.GetUtcNow());
        return outcome switch
        {
            CrewCodeOutcome.Ok => Ok(owner),
            CrewCodeOutcome.OwnCode => OwnCode(),
            _ => InvalidCode(),
        };
    }

    /// <summary>
    /// Redeems a crew code: the caller and the code's owner become crew straight away. The code
    /// works once; if several people hold it, the first to redeem wins and the rest get a 404.
    /// Redeeming for someone who is already crew succeeds and still uses the code up.
    /// </summary>
    [HttpPost("/me/crew/codes/{code}/redeem")]
    [ProducesResponseType(typeof(CrewMember), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult RedeemCode(string code)
    {
        if (Gate(out var user) is { } denied)
            return denied;

        if (!limiter.TryAccept(user.UserId))
            return TooManyAttempts();

        var (outcome, owner) = store.RedeemCrewCode(user.UserId, code, timeProvider.GetUtcNow());
        return outcome switch
        {
            CrewCodeOutcome.Ok => Ok(owner),
            CrewCodeOutcome.OwnCode => OwnCode(),
            CrewCodeOutcome.CrewFull => Conflict(new { error = "Crew is full, so this person can't be added." }),
            _ => InvalidCode(),
        };
    }

    // ---- Find new crew ----------------------------------------------------------------------
    // Adding crew in person, without a code. See CrewFinder and .ai/plans/crew-finding.md.

    /// <summary>
    /// Turns "Find new crew" on at the caller's position for two minutes (restarting it if it was
    /// already on). Returns who else nearby has it on.
    /// </summary>
    [HttpPost("/me/crew/finding")]
    [ProducesResponseType(typeof(CrewFindingState), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult StartFinding([FromBody] CrewFindingRequest? request)
    {
        if (Gate(out var user) is { } denied)
            return denied;
        if (ToFix(request) is not { } fix)
            return InvalidPosition();

        if (finder.Start(user.UserId, fix) == CrewFinder.StartOutcome.TooMany)
            return TooManyAttempts();

        return FindingState(user.UserId);
    }

    /// <summary>
    /// A fresh position while "Find new crew" is on; clients send one every few seconds. 410 once the
    /// window has ended (two minutes, or switched off).
    /// </summary>
    [HttpPut("/me/crew/finding")]
    [ProducesResponseType(typeof(CrewFindingState), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult RefreshFinding([FromBody] CrewFindingRequest? request)
    {
        if (Gate(out var user) is { } denied)
            return denied;
        if (ToFix(request) is not { } fix)
            return InvalidPosition();

        return finder.Refresh(user.UserId, fix) switch
        {
            CrewFinder.RefreshOutcome.NotOn => FindingEnded(),
            CrewFinder.RefreshOutcome.TooMany => TooManyAttempts(),
            _ => FindingState(user.UserId),
        };
    }

    /// <summary>Turns "Find new crew" off. Unanswered accepts are dropped. Succeeds when it was already off.</summary>
    [HttpDelete("/me/crew/finding")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult StopFinding()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        finder.Stop(user.UserId);
        return NoContent();
    }

    /// <summary>
    /// The caller accepts someone from their nearby list. When that person has already accepted the
    /// caller, the two become crew straight away and both see them in <c>added</c>. 404 when that person
    /// isn't on or nearby any more (or the two are blocked); 409 when either crew is full; 410 when the
    /// caller's own window has ended.
    /// </summary>
    [HttpPost("/me/crew/finding/{id}/accept")]
    [ProducesResponseType(typeof(CrewFindingState), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult AcceptFinding(string id)
    {
        if (Gate(out var user) is { } denied)
            return denied;
        if (!finder.IsOn(user.UserId))
            return FindingEnded();

        // Apply the caller's block and crew filters before recording anything, so a blocked person's
        // token behaves exactly like one that isn't there.
        var otherId = finder.ResolveNearby(user.UserId, id);
        if (otherId is null || store.GetBlockedPartyIds(user.UserId).Contains(otherId))
            return PersonGone();

        var (outcome, _) = finder.Accept(user.UserId, id);
        switch (outcome)
        {
            case CrewFinder.AcceptOutcome.NotOn:
                return FindingEnded();
            case CrewFinder.AcceptOutcome.NotFound:
                return PersonGone();
            case CrewFinder.AcceptOutcome.Mutual:
                var (link, forMe, forThem) = store.AddCrewLink(user.UserId, otherId, timeProvider.GetUtcNow());
                if (link == CrewLinkOutcome.CrewFull)
                    return Conflict(new { error = "Crew is full, so this person can't be added." });
                if (link != CrewLinkOutcome.Ok || forMe is null || forThem is null)
                    return PersonGone();
                finder.RecordAdded(user.UserId, forMe);
                finder.RecordAdded(otherId, forThem);
                break;
        }

        return FindingState(user.UserId);
    }

    private IActionResult FindingState(string userId)
    {
        if (finder.ExpiresAt(userId) is not { } expiresAt)
            return FindingEnded();

        // Strangers who blocked (or were blocked by) the caller, and people already in the crew, aren't
        // listed: there is nothing to add, and a blocked person must not learn the caller is here.
        var blocked = store.GetBlockedPartyIds(userId);
        var crewIds = store.GetCrewIds(userId);
        var people = new List<CrewNearbyPerson>();
        foreach (var candidate in finder.Nearby(userId))
        {
            if (blocked.Contains(candidate.UserId) || crewIds.Contains(candidate.UserId))
                continue;
            if (store.GetCrewIdentity(candidate.UserId) is not { } identity)
                continue;
            people.Add(new CrewNearbyPerson(
                candidate.Token, identity.Nickname, identity.DisplayName, candidate.YouAccepted, candidate.AcceptedYou));
        }

        return Ok(new CrewFindingState(
            expiresAt.ToUniversalTime().ToString("O"), people, finder.Added(userId)));
    }

    private static CrewFinder.Fix? ToFix(CrewFindingRequest? request) =>
        request is { Latitude: { } lat, Longitude: { } lon }
        && NearbyJoin.IsValidPosition(lat, lon, request.Accuracy)
            ? new CrewFinder.Fix(lat, lon, request.Accuracy)
            : null;

    private BadRequestObjectResult InvalidPosition() =>
        BadRequest(new { error = "Find new crew needs your current position, accurate to 100 m or better." });

    private ObjectResult FindingEnded() =>
        StatusCode(StatusCodes.Status410Gone, new { error = "Find new crew has turned off." });

    private NotFoundObjectResult PersonGone() =>
        NotFound(new { error = "That person isn't nearby any more." });

    /// <summary>Gets the name the caller's crew sees for them (null when unset).</summary>
    [HttpGet("/me/profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetProfile()
    {
        if (Gate(out var user) is { } denied)
            return denied;

        return Ok(new ProfileResponse(store.GetNickname(user.UserId)));
    }

    /// <summary>Sets or clears (null or blank) the nickname the caller's crew sees. 1-30 characters.</summary>
    [HttpPut("/me/profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult UpdateProfile([FromBody] UpdateProfileRequest? request)
    {
        if (Gate(out var user) is { } denied)
            return denied;

        var raw = request?.Nickname;
        var nickname = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        if (nickname is not null && (nickname.Length > MaxNicknameLength || nickname.Any(char.IsControl)))
            return BadRequest(new { error = $"Nickname must be 1-{MaxNicknameLength} characters with no control characters." });

        store.SetNickname(user.UserId, nickname);
        return Ok(new ProfileResponse(nickname));
    }

    // 401 when not signed in. 404 (not 403) when an admin has switched crew off, so the endpoints
    // look absent: shipped iOS builds treat any status except 401/426 from /me/crew as "this server
    // has no crew" and hide it quietly, whereas a 401 would be handled as a signed-out session.
    private IActionResult? Gate(out AuthenticatedUser user)
    {
        if (!userAuth.TryAuthenticate(Request, out user))
            return Unauthorized();

        return features.IsOn(Features.Crew) ? null : NotFound();
    }

    private ObjectResult InvalidCode() =>
        NotFound(new { error = "That crew code isn't valid any more." });

    private ObjectResult OwnCode() =>
        BadRequest(new { error = "That's your own crew code." });

    private ObjectResult TooManyAttempts() =>
        StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Try again later." });
}
