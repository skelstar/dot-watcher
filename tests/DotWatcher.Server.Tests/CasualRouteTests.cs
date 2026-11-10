using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace DotWatcher.Server.Tests;

// A GPX route on a casual run: set by the runner, seen by them and by anyone with a watch link, and
// gone when the run ends. See .ai/plans/casual-tracking.md.
public class CasualRouteTests
{
    private const string Gpx =
        """<gpx version="1.1"><trk><trkseg><trkpt lat="-41.17" lon="174.77"/><trkpt lat="-41.18" lon="174.78"/></trkseg></trk></gpx>""";

    private static DotWatcherApiFactory CasualFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual] };

    [Fact]
    public async Task SetRoute_WhenNotOut_IsAConflict()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        var response = await PostRouteAsync(client, alice.AccessToken, Gpx);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetRoute_WithAnEmptyBody_IsRejected()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);

        var response = await PostRouteAsync(client, alice.AccessToken, "  ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheRunner_SeesTheirOwnRoute_InLiveAndCanFetchIt()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, (await PostRouteAsync(client, alice.AccessToken, Gpx)).StatusCode);

        var live = await GetLiveAsync(client, alice.AccessToken);
        var route = Assert.Single(live.Routes!);
        Assert.Equal("AL", route.RunnerName);

        var fetched = await GetAsync(client, $"/casual/runs/{route.RunId}/route", alice.AccessToken);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(Gpx, await fetched.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SomeoneWhoCantSeeTheRunner_CantFetchTheirRoute()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");
        await StartAndPostAsync(client, alice.AccessToken);
        await PostRouteAsync(client, alice.AccessToken, Gpx);
        var runId = (await GetLiveAsync(client, alice.AccessToken)).Routes!.Single().RunId;

        var fetched = await GetAsync(client, $"/casual/runs/{runId}/route", bob.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task DeleteRoute_RemovesIt()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        await PostRouteAsync(client, alice.AccessToken, Gpx);

        using var delete = AuthTestHelpers.WithUserToken(HttpMethod.Delete, "/casual/route", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);

        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Routes!);
    }

    [Fact]
    public async Task WatchLink_ShowsAndServesTheRoute_UntilTheRunEnds()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        Assert.False((await client.GetFromJsonAsync<CasualWatchView>(link.Path))!.HasRoute);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{link.Path}/route")).StatusCode);

        await PostRouteAsync(client, alice.AccessToken, Gpx);

        Assert.True((await client.GetFromJsonAsync<CasualWatchView>(link.Path))!.HasRoute);
        var route = await client.GetAsync($"{link.Path}/route");
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Equal(Gpx, await route.Content.ReadAsStringAsync());

        using var stop = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/stop", alice.AccessToken);
        await client.SendAsync(stop);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{link.Path}/route")).StatusCode);
    }

    [Fact]
    public async Task ANewRun_StartsWithNoRoute()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        await PostRouteAsync(client, alice.AccessToken, Gpx);
        using var stop = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/stop", alice.AccessToken);
        await client.SendAsync(stop);

        await StartAndPostAsync(client, alice.AccessToken);

        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Routes!);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> PostRouteAsync(HttpClient client, string accessToken, string gpx)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/route", accessToken);
        request.Content = new StringContent(gpx, Encoding.UTF8, "application/gpx+xml");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, url, accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<CasualLiveResponse> GetLiveAsync(HttpClient client, string accessToken)
    {
        var response = await GetAsync(client, "/casual/live", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualLiveResponse>())!;
    }

    private static async Task StartAndPostAsync(HttpClient client, string accessToken)
    {
        using var start = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(start)).StatusCode);
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate("ignored", SessionStore.CasualSessionId, -41.17, 174.7762, 90, DateTimeOffset.UtcNow),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    private static async Task<CasualWatchLinkResponse> CreateLinkAsync(HttpClient client, string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/watch-link", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualWatchLinkResponse>())!;
    }
}
