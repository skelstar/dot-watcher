using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DotWatcher.Server.Tests;

internal static class AuthTestHelpers
{
    internal static async Task<string> RegisterAsync(
        HttpClient client,
        string username,
        string displayName)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username,
            password = "correct-horse-password",
            displayName,
        });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    internal static async Task<SessionMembership> CreateSessionAsync(
        HttpClient client,
        string accessToken,
        string? sessionCode = "SUNSET23",
        string? displayName = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = JsonContent.Create(new { sessionCode, displayName }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    internal static async Task<SessionMembership> JoinSessionAsync(
        HttpClient client,
        string accessToken,
        string inviteCode,
        string role = "viewer",
        string? displayName = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/session-invites/{inviteCode}/join")
        {
            Content = JsonContent.Create(new { role, displayName }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    internal static HttpRequestMessage WithUserToken(
        HttpMethod method,
        string uri,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}
