using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BotDots.Service;

public enum PostOutcome
{
    Ok,
    // 403: the casual run was closed (stopped, or abandoned). Start again and retry once, as the apps do.
    NoRun,
    // 401: the token is no good any more. Sign in again.
    SignedOut,
    Failed,
}

/// <summary>
/// The DotWatcher server, used exactly as a phone uses it (sign in, start casual, post a location), plus the
/// admin bearer for the two things only the service does: creating <c>botdot-</c> accounts and reading the
/// spawn cells. See .ai/plans/bot-dot-army.md.
/// </summary>
public sealed class BotServerClient(HttpClient http, IOptions<BotDotsOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string AdminBearer => options.Value.AdminBearer;

    /// <summary>The cells to keep bot-dots in. Null when the server has bot-dots switched off (404).</summary>
    public async Task<IReadOnlyList<CellInfo>?> GetCellsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/casual/cells");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminBearer);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CellInfo>>(Json, cancellationToken) ?? [];
    }

    /// <summary>
    /// A token for a pool account, creating the account the first time. Registers first: an existing account
    /// answers 409 at no cost, whereas a failed sign-in would count toward the server's lockout. The password is
    /// derived from the admin bearer, so nothing needs storing and a restart signs straight back in.
    /// Null when the server refuses (bot-dots switched off, or the wrong bearer).
    /// </summary>
    public async Task<string?> SignInAsync(string username, CancellationToken cancellationToken)
    {
        var password = PasswordFor(username);
        using (var register = new HttpRequestMessage(HttpMethod.Post, "/auth/register"))
        {
            register.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminBearer);
            register.Content = JsonContent.Create(new { username, password, displayName = "XX" }, options: Json);
            using var response = await http.SendAsync(register, cancellationToken);
            if (response.IsSuccessStatusCode)
                return await ReadTokenAndShowNearbyAsync(response, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Conflict)
                return null;
        }

        using var login = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { username, password }, options: Json),
        };
        using var loginResponse = await http.SendAsync(login, cancellationToken);
        return loginResponse.IsSuccessStatusCode ? await ReadTokenAndShowNearbyAsync(loginResponse, cancellationToken) : null;
    }

    /// <summary>Starts (or continues) the account's casual run; returns the session id to post to, or null.</summary>
    public async Task<string?> StartAsync(string token, CancellationToken cancellationToken)
    {
        using var request = WithToken(HttpMethod.Post, "/casual/start", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var start = await response.Content.ReadFromJsonAsync<StartResponse>(Json, cancellationToken);
        return start?.SessionId;
    }

    public async Task<PostOutcome> PostLocationAsync(
        string token,
        string sessionId,
        LatLon position,
        double heading,
        DateTimeOffset timestamp,
        DateTimeOffset nextExpectedAt,
        CancellationToken cancellationToken)
    {
        using var request = WithToken(HttpMethod.Post, "/location", token);
        request.Content = JsonContent.Create(new
        {
            sessionId,
            latitude = position.Lat,
            longitude = position.Lon,
            heading = Math.Round(Geo.NormalizeDegrees(heading)),
            timestamp,
            nextExpectedAt,
        }, options: Json);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => PostOutcome.Ok,
                HttpStatusCode.Forbidden => PostOutcome.NoRun,
                HttpStatusCode.Unauthorized => PostOutcome.SignedOut,
                _ => PostOutcome.Failed,
            };
        }
        catch (HttpRequestException)
        {
            return PostOutcome.Failed;
        }
    }

    /// <summary>Ends the account's casual run, so its dot leaves everyone's map at once.</summary>
    public async Task StopAsync(string token, CancellationToken cancellationToken)
    {
        using var request = WithToken(HttpMethod.Post, "/casual/stop", token);
        using var _ = await http.SendAsync(request, cancellationToken);
    }

    public string PasswordFor(string username) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AdminBearer), Encoding.UTF8.GetBytes(username)))[..32];

    // Bot-dots must be visible to people nearby (that is the point of them), so this is set on every sign-in.
    private async Task<string?> ReadTokenAndShowNearbyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, cancellationToken);
        if (string.IsNullOrEmpty(auth?.AccessToken))
            return null;

        using var settings = WithToken(HttpMethod.Put, "/me/casual/settings", auth.AccessToken);
        settings.Content = JsonContent.Create(new { visibleToNonCrew = true }, options: Json);
        using var settingsResponse = await http.SendAsync(settings, cancellationToken);
        return settingsResponse.IsSuccessStatusCode ? auth.AccessToken : null;
    }

    private static HttpRequestMessage WithToken(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record AuthResponse(string AccessToken);

    private sealed record StartResponse(string SessionId, string RunId);
}
