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
    /// Redeeming for someone who is already crew succeeds without using the code up.
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

    // 401 when not signed in. 404 (not 403) when the account doesn't have crew yet, so the endpoints
    // look absent: shipped iOS builds treat any status except 401/426 from /me/crew as "this server
    // has no crew" and hide it quietly, whereas a 401 would be handled as a signed-out session.
    private IActionResult? Gate(out AuthenticatedUser user)
    {
        if (!userAuth.TryAuthenticate(Request, out user))
            return Unauthorized();

        return features.IsEnabled(user.UserId, Features.Crew) ? null : NotFound();
    }

    private ObjectResult InvalidCode() =>
        NotFound(new { error = "That crew code isn't valid any more." });

    private ObjectResult OwnCode() =>
        BadRequest(new { error = "That's your own crew code." });

    private ObjectResult TooManyAttempts() =>
        StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many attempts. Try again later." });
}
