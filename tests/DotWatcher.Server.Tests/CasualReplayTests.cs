using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Admin replay of casual ("Go") runs: GET /admin/casual/runs and a run's recording/meta.
public class CasualReplayTests
{
    private const string AdminToken = "test-token";
    private const double Latitude = -41.17;
    private const double Longitude = 174.7762;

    private static DotWatcherApiFactory CasualFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual] };

    [Fact]
    public async Task Runs_NeedTheAdminToken()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "Alice");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/casual/runs")).StatusCode);
        // A signed-in user's own token isn't enough either.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, "/admin/casual/runs", alice.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Runs_ListTheRunWithItsRunnerAndPositionCount()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "Alice");
        var runId = await StartAsync(client, alice.AccessToken);
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2);
        await PostAsync(client, alice.AccessToken, t0);
        await PostAsync(client, alice.AccessToken, t0.AddMinutes(1));

        var runs = await GetRunsAsync(client);

        var run = Assert.Single(runs);
        Assert.Equal(runId, run.RunId);
        Assert.Equal("alice", run.Username);
        Assert.Equal("Alice", run.DisplayName);
        Assert.Null(run.EndedAt);
        Assert.Equal(2, run.PositionCount);
    }

    [Fact]
    public async Task Recording_ReturnsOnlyThatRunsPositions_InOrder()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "Alice");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "Bob");

        var firstRun = await StartAsync(client, alice.AccessToken);
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        await PostAsync(client, alice.AccessToken, t0);
        await PostAsync(client, alice.AccessToken, t0.AddMinutes(1));
        await SendAsync(client, "/casual/stop", alice.AccessToken, HttpMethod.Post);

        // A second run by Alice and a concurrent one by Bob must not leak into the first's replay.
        await StartAsync(client, alice.AccessToken);
        await PostAsync(client, alice.AccessToken, t0.AddMinutes(3));
        await StartAsync(client, bob.AccessToken);
        await PostAsync(client, bob.AccessToken, t0.AddMinutes(1));

        var response = await SendAsync(client, $"/admin/casual/runs/{firstRun}/recording", AdminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Contains("\"runnerName\":\"Alice\"", line));

        var meta = (await (await SendAsync(client, $"/admin/casual/runs/{firstRun}/recording/meta", AdminToken))
            .Content.ReadFromJsonAsync<RecordingMeta>())!;
        Assert.Equal(t0.ToUnixTimeSeconds(), meta.RunStartTimestamp!.Value.ToUnixTimeSeconds());
        Assert.Equal(t0.AddMinutes(1).ToUnixTimeSeconds(), meta.LatestTimestamp!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Recording_HonoursTheWindow()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "Alice");
        var runId = await StartAsync(client, alice.AccessToken);
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        await PostAsync(client, alice.AccessToken, t0);
        await PostAsync(client, alice.AccessToken, t0.AddMinutes(2));

        var since = Uri.EscapeDataString(t0.AddMinutes(1).ToString("O"));
        var response = await SendAsync(client, $"/admin/casual/runs/{runId}/recording?since={since}", AdminToken);

        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public async Task UnknownRun_IsNotFound()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, $"/admin/casual/runs/{unknown}/recording/meta", AdminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, $"/admin/casual/runs/{unknown}/recording", AdminToken)).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string url, string token, HttpMethod? method = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method ?? HttpMethod.Get, url, token);
        return await client.SendAsync(request);
    }

    private static async Task<string> StartAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, "/casual/start", accessToken, HttpMethod.Post);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualStartResponse>())!.RunId;
    }

    private static async Task PostAsync(HttpClient client, string accessToken, DateTimeOffset timestamp)
    {
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate("ignored", SessionStore.CasualSessionId, Latitude, Longitude, 90, timestamp),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    private static async Task<IReadOnlyList<AdminCasualRun>> GetRunsAsync(HttpClient client)
    {
        var response = await SendAsync(client, "/admin/casual/runs", AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<AdminCasualRun>>())!;
    }
}
