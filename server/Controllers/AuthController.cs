using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class AuthController(SessionStore store, UserTokenAuth tokenAuth) : ControllerBase
{
    [HttpPost("/auth/register")]
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

    [HttpPost("/auth/login")]
    public IActionResult Login([FromBody] LoginRequest? request)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        var username = NormalizeUsername(request.Username);
        if (username is null || request.Password is null)
            return Unauthorized();

        var account = store.GetUserByUsername(username);
        if (account is null || !PasswordHasher.Verify(request.Password, account.PasswordHash))
            return Unauthorized();

        return Ok(ToResponse(account));
    }

    private AuthResponse ToResponse(UserAccount account) =>
        new(
            tokenAuth.CreateToken(account),
            new AuthenticatedUser(account.Id, account.Username, account.DisplayName));

    private static string? NormalizeUsername(string? value)
    {
        var username = value?.Trim().ToLowerInvariant();
        if (username is null)
            return null;

        return username.Length is >= 3 and <= 64 ? username : null;
    }
}
