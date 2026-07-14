using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DotWatcher.Server.Tests;

// Covers the X-Api-Version gate in Program.cs: old/missing-header clients keep working, clients
// below a configured floor are rejected, and admin/ops bearer-token traffic always bypasses it.
public class ApiVersionGateTests
{
    [Fact]
    public async Task Request_WithNoApiVersionHeader_IsAllowedEvenWithAHighFloorConfigured()
    {
        using var baseFactory = new DotWatcherApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(WithMinimumApiVersion(5));
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/me/sessions", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_BelowFloor_OnUserTokenEndpoint_ReturnsUpgradeRequired()
    {
        using var baseFactory = new DotWatcherApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(WithMinimumApiVersion(5));
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/me/sessions", token);
        request.Headers.Add("X-Api-Version", "1");
        var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task Request_AtOrAboveFloor_OnUserTokenEndpoint_IsAllowed()
    {
        using var baseFactory = new DotWatcherApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(WithMinimumApiVersion(5));
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/me/sessions", token);
        request.Headers.Add("X-Api-Version", "5");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_BelowFloor_OnAdminBearerEndpoint_BypassesTheGate()
    {
        using var baseFactory = new DotWatcherApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(WithMinimumApiVersion(5));
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        request.Headers.Add("X-Api-Version", "1");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Action<IWebHostBuilder> WithMinimumApiVersion(int minimumApiVersion) => builder =>
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinimumApiVersion"] = minimumApiVersion.ToString(),
            }));
}
