using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotWatcher.Server;

public sealed class UserTokenAuth
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SessionStore _store;
    private readonly byte[] _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _accessTokenLifetime;
    private readonly TimeSpan _clockSkew;

    public UserTokenAuth(IConfiguration configuration, SessionStore store)
    {
        _store = store;

        var signingKey = configuration["JwtSigningKey"]
            ?? throw new InvalidOperationException(
                "JwtSigningKey is not configured. Set it via appsettings or the JwtSigningKey environment variable.");

        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("JwtSigningKey must be at least 32 bytes.");

        _signingKey = Encoding.UTF8.GetBytes(signingKey);
        _issuer = configuration.GetValue<string>("JwtIssuer", "dot-watcher")!;
        _audience = configuration.GetValue<string>("JwtAudience", "dot-watcher")!;
        _accessTokenLifetime = TimeSpan.FromMinutes(
            Math.Clamp(configuration.GetValue<int>("JwtAccessTokenMinutes", 60), 1, 1440));
        _clockSkew = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue<int>("JwtClockSkewSeconds", 60), 0, 300));
    }

    public IssuedUserToken CreateToken(UserAccount account)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(_accessTokenLifetime);
        var tokenId = Guid.NewGuid().ToString("N");
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT",
        };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = _issuer,
            ["aud"] = _audience,
            ["sub"] = account.Id,
            ["username"] = account.Username,
            ["name"] = account.DisplayName,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = tokenId,
        };

        var headerPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, _jsonOptions));
        var payloadPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions));
        var signingInput = $"{headerPart}.{payloadPart}";
        var signature = Sign(signingInput);
        return new IssuedUserToken($"{signingInput}.{signature}", expiresAt, tokenId);
    }

    public bool TryAuthenticate(HttpRequest request, out AuthenticatedUser user)
    {
        return TryAuthenticate(request, out user, out _);
    }

    public bool TryAuthenticate(
        HttpRequest request,
        out AuthenticatedUser user,
        out ValidatedUserToken token)
    {
        user = default!;
        token = default!;

        if (!request.Headers.TryGetValue("Authorization", out var auth))
            return false;

        const string prefix = "Bearer ";
        var value = auth.ToString();
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return TryValidateToken(value[prefix.Length..], out user, out token);
    }

    private bool TryValidateToken(
        string token,
        out AuthenticatedUser user,
        out ValidatedUserToken validatedToken)
    {
        user = default!;
        validatedToken = default!;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        var signingInput = $"{parts[0]}.{parts[1]}";
        var expectedSignature = Sign(signingInput);
        if (!FixedTimeEquals(parts[2], expectedSignature))
            return false;

        JwtHeader header;
        JwtPayload payload;
        try
        {
            header = JsonSerializer.Deserialize<JwtHeader>(
                Base64UrlDecode(parts[0]),
                _jsonOptions) ?? new();
            payload = JsonSerializer.Deserialize<JwtPayload>(
                Base64UrlDecode(parts[1]),
                _jsonOptions) ?? new();
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (header.Alg != "HS256" ||
            header.Typ != "JWT" ||
            payload.Iss != _issuer ||
            payload.Aud != _audience ||
            payload.Nbf > now.Add(_clockSkew).ToUnixTimeSeconds() ||
            payload.Exp <= now.Subtract(_clockSkew).ToUnixTimeSeconds() ||
            payload.Iat <= 0 ||
            string.IsNullOrWhiteSpace(payload.Sub) ||
            string.IsNullOrWhiteSpace(payload.Username) ||
            string.IsNullOrWhiteSpace(payload.Name) ||
            string.IsNullOrWhiteSpace(payload.Jti))
        {
            return false;
        }

        if (_store.IsUserTokenRevoked(payload.Jti, now))
            return false;

        user = new AuthenticatedUser(payload.Sub, payload.Username, payload.Name);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
        validatedToken = new ValidatedUserToken(
            payload.Jti,
            expiresAt,
            expiresAt.Add(_clockSkew));
        return true;
    }

    private string Sign(string signingInput)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput)));
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

    private sealed record JwtHeader
    {
        public string? Alg { get; init; }
        public string? Typ { get; init; }
    }

    private sealed record JwtPayload
    {
        public string? Iss { get; init; }
        public string? Aud { get; init; }
        public string? Sub { get; init; }
        public string? Username { get; init; }
        public string? Name { get; init; }
        public string? Jti { get; init; }
        public long Iat { get; init; }
        public long Nbf { get; init; }
        public long Exp { get; init; }
    }
}
