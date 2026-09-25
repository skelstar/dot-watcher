using System.Security.Cryptography;
using System.Text;

namespace DotWatcher.Server;

/// <summary>
/// Short-lived tokens that authorize uploading a GPX route to exactly one session. The iOS app mints
/// one (as the session owner) and hands it to the web upload page in a URL fragment, since the web
/// client has no sign-in. Format: base64url("{sessionId}|{expiresUnixSeconds}").base64url(HMAC-SHA256).
/// Deliberately not a JWT, so it can never be mistaken for a user access token.
/// </summary>
public sealed class RouteUploadTokenAuth
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly byte[] _signingKey;

    public RouteUploadTokenAuth(IConfiguration configuration)
    {
        var signingKey = configuration["JwtSigningKey"]
            ?? throw new InvalidOperationException(
                "JwtSigningKey is not configured. Set it via appsettings or the JwtSigningKey environment variable.");
        _signingKey = Encoding.UTF8.GetBytes(signingKey);
    }

    public IssuedRouteUploadToken CreateToken(string sessionId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes($"{sessionId}|{expiresAt.ToUnixTimeSeconds()}"));
        return new IssuedRouteUploadToken($"{payload}.{Sign(payload)}", expiresAt);
    }

    /// <summary>True when the request carries a valid, unexpired upload token for <paramref name="sessionId"/>.</summary>
    public bool IsAuthorized(HttpRequest request, string sessionId)
    {
        if (!request.Headers.TryGetValue("Authorization", out var auth))
            return false;

        const string prefix = "Bearer ";
        var value = auth.ToString();
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = value[prefix.Length..].Split('.');
        if (parts.Length != 2)
            return false;

        if (!FixedTimeEquals(parts[1], Sign(parts[0])))
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        }
        catch (FormatException)
        {
            return false;
        }

        // Session ids are generated server-side and never contain '|'; split on the last one anyway.
        var separator = decoded.LastIndexOf('|');
        if (separator < 0 || !long.TryParse(decoded[(separator + 1)..], out var expiresUnix))
            return false;

        return decoded[..separator] == sessionId
            && expiresUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes($"route-upload.{payload}")));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => "",
            _ => throw new FormatException("Invalid base64url value."),
        };
        return Convert.FromBase64String(padded);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public sealed record IssuedRouteUploadToken(string Token, DateTimeOffset ExpiresAt);
