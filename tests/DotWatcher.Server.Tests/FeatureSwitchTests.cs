using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Global kill switches for crew, casual and events (PUT /admin/features/{feature}).
public class FeatureSwitchTests
{
    [Fact]
    public async Task EveryFeature_IsOnByDefault()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var switches = (await (await SendAsync(client, HttpMethod.Get, "/admin/features", "test-token"))
            .Content.ReadFromJsonAsync<List<FeatureSwitch>>())!;

        Assert.Equal(new[] { "crew", "casual", "events" }, switches.Select(s => s.Feature));
        Assert.All(switches, s => Assert.True(s.Enabled));
    }

    [Theory]
    [InlineData("crew", "/me/crew")]
    [InlineData("casual", "/casual/live")]
    [InlineData("events", "/me/events/nearby")]
    public async Task SwitchingAFeatureOff_MakesItsEndpointsNotFound_AndOnRestoresThem(string feature, string path)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        await SetSwitchAsync(client, feature, false);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, path, alice.AccessToken)).StatusCode);

        await SetSwitchAsync(client, feature, true);
        Assert.NotEqual(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, path, alice.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task SwitchingOffOneFeature_LeavesTheOthersOn()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        await SetSwitchAsync(client, "crew", false);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/me/events/nearby", alice.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task SetSwitch_RequiresAdminTokenAKnownFeatureAndEnabled()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Put, "/admin/features/crew", "wrong", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/admin/features", "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, "/admin/features/nope", "test-token", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Put, "/admin/features/crew", "test-token", new { })).StatusCode);
    }

    private static async Task SetSwitchAsync(HttpClient client, string feature, bool enabled) =>
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/admin/features/{feature}", "test-token", new { enabled })).StatusCode);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
