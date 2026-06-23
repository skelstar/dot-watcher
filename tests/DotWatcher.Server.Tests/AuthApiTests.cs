using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class AuthApiTests
{
    [Fact]
    public async Task Register_WithValidInput_ReturnsAccessTokenAndUser()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username = "alice",
            password = "correct-horse-password",
            displayName = "Alice",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("accessToken").GetString()));
        Assert.True(body.RootElement.TryGetProperty("expiresAt", out var expiresAt));
        Assert.True(DateTimeOffset.Parse(expiresAt.GetString()!) > DateTimeOffset.UtcNow);
        Assert.Equal("alice", body.RootElement.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal("Alice", body.RootElement.GetProperty("user").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Register_WithDuplicateUsername_ReturnsConflict()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username = "ALICE",
            password = "correct-horse-password",
            displayName = "Alice Again",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessToken()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            username = "alice",
            password = "correct-horse-password",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("accessToken").GetString()));
        Assert.True(body.RootElement.TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            username = "alice",
            password = "wrong-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterRepeatedInvalidCredentials_ReturnsTooManyRequests()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        for (var i = 0; i < 5; i++)
        {
            var failed = await client.PostAsJsonAsync("/auth/login", new
            {
                username = "alice",
                password = "wrong-password",
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("/auth/login", new
        {
            username = "alice",
            password = "correct-horse-password",
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Logout_RevokesCurrentAccessToken()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logoutResponse = await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var sessions = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/me/sessions", token);
        var sessionsResponse = await client.SendAsync(sessions);

        Assert.Equal(HttpStatusCode.Unauthorized, sessionsResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_RemovesAccountOwnedSessionsAndInvalidatesToken()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);
        await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            token);

        using var delete = AuthTestHelpers.WithUserToken(HttpMethod.Delete, "/me", token);
        var deleteResponse = await client.SendAsync(delete);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var sessions = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/me/sessions", token);
        var sessionsResponse = await client.SendAsync(sessions);
        Assert.Equal(HttpStatusCode.Unauthorized, sessionsResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            username = "alice",
            password = "correct-horse-password",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        using var recording = new HttpRequestMessage(
            HttpMethod.Get,
            $"/sessions/{session.SessionId}/recording");
        recording.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var recordingResponse = await client.SendAsync(recording);
        Assert.Equal(HttpStatusCode.NotFound, recordingResponse.StatusCode);
    }
}
