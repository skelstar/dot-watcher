using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class AuthController(
    SessionStore store,
    UserTokenAuth tokenAuth,
    AuthAttemptLimiter attemptLimiter) : ControllerBase
{
    /// <summary>Creates a new account and returns an access token.</summary>
    [HttpPost("/auth/register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Register([FromBody] RegisterRequest? request)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        var username = NormalizeUsername(request.Username);
        var displayName = request.DisplayName?.Trim() ?? "";

        if (username is null)
            return BadRequest(new { error = "Username must be 3-64 characters." });

        if (request.Password is null || request.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters." });

        if (displayName.Length is < 1 or > 80)
            return BadRequest(new { error = "Display name must be 1-80 characters." });

        var account = new UserAccount(
            Id: Guid.NewGuid().ToString("N"),
            Username: username,
            DisplayName: displayName,
            PasswordHash: PasswordHasher.Hash(request.Password));

        if (!store.CreateUser(account))
            return Conflict(new { error = "Username is already registered." });

        return Ok(ToResponse(account));
    }

    /// <summary>Exchanges credentials for an access token. Locks out after repeated failures.</summary>
    [HttpPost("/auth/login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Login([FromBody] LoginRequest? request)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        var username = NormalizeUsername(request.Username);
        if (username is null || request.Password is null)
            return Unauthorized();

        // The request body is deliberately excluded from the response-summary log for /auth
        // paths (it carries the plaintext password), so the attempted username is surfaced here
        // instead - via the same "Log:" HttpContext.Items convention the summary logger reads.
        HttpContext.Items["Log:LoginUsername"] = username;

        var attemptKey = AuthAttemptLimiter.KeyFor(HttpContext, username);
        if (attemptLimiter.IsLocked(attemptKey, out var retryAfter))
        {
            HttpContext.Items["Log:LoginResult"] = "locked";
            Response.Headers["Retry-After"] = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many failed login attempts." });
        }

        var account = store.GetUserByUsername(username);
        if (account is null || !PasswordHasher.Verify(request.Password, account.PasswordHash))
        {
            attemptLimiter.RecordFailure(attemptKey);
            HttpContext.Items["Log:LoginResult"] = "failed";
            return Unauthorized();
        }

        attemptLimiter.RecordSuccess(attemptKey);
        HttpContext.Items["Log:LoginResult"] = "success";
        HttpContext.Items["Log:LoginUserId"] = account.Id;
        return Ok(ToResponse(account));
    }

    /// <summary>Revokes the caller's current access token.</summary>
    [HttpPost("/auth/logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        if (tokenAuth.TryAuthenticate(Request, out _, out var token))
            store.RevokeUserToken(token.TokenId, token.AcceptedUntil);

        return NoContent();
    }

    /// <summary>Permanently deletes the caller's account and any sessions it owns.</summary>
    [HttpDelete("/me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult DeleteAccount()
    {
        if (!tokenAuth.TryAuthenticate(Request, out var user, out var token))
            return Unauthorized();

        store.DeleteUserAccount(user.UserId);
        store.RevokeUserToken(token.TokenId, token.AcceptedUntil);
        return NoContent();
    }

    private AuthResponse ToResponse(UserAccount account)
    {
        var token = tokenAuth.CreateToken(account);
        return new AuthResponse(
            token.Value,
            token.ExpiresAt,
            new AuthenticatedUser(account.Id, account.Username, account.DisplayName));
    }

    private static string? NormalizeUsername(string? value)
    {
        var username = value?.Trim().ToLowerInvariant();
        if (username is null)
            return null;

        return username.Length is >= 3 and <= 64 ? username : null;
    }
}
