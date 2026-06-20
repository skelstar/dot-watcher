namespace DotWatcher.Server;

public sealed class BearerTokenAuth
{
    private readonly string _bearerToken;

    public BearerTokenAuth(IConfiguration configuration)
    {
        _bearerToken = configuration["BearerToken"]
            ?? throw new InvalidOperationException(
                "BearerToken is not configured. Set it via appsettings or the BearerToken environment variable.");
    }

    public bool IsAuthorized(HttpRequest request) =>
        request.Headers.TryGetValue("Authorization", out var auth) &&
        auth.ToString() == $"Bearer {_bearerToken}";
}
