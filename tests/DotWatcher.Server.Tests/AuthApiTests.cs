using System.Net;
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
}
