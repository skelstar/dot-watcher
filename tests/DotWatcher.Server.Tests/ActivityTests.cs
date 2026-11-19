using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotWatcher.Server.Tests;

// Activity events for the admin Activity tab's flow. See .ai/plans/activity-flow.md and
// server/Activity/activity-events.json.
public class ActivityTests
{
    private const double Latitude = -41.17;
    private const double Longitude = 174.7762;
    private const string AdminToken = "test-token";

    private static DotWatcherApiFactory CasualFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual] };

    // ---- The register --------------------------------------------------------------------------

    [Fact]
    public void Register_EntriesAreWellFormed()
    {
        var events = ActivityRegister.Events;
        Assert.NotEmpty(events);

        var kinds = events.Select(e => e.Kind).ToList();
        Assert.Equal(kinds.Count, kinds.Distinct().Count());

        foreach (var entry in events)
        {
            Assert.Matches(new Regex("^[a-z][a-z0-9_]*$"), entry.Kind);
            Assert.False(string.IsNullOrWhiteSpace(entry.Label), $"{entry.Kind} has no label");
            Assert.Contains(entry.Shows, new[] { "line", "branch" });
            Assert.Contains(entry.Status, new[] { "wanted", "recording" });

            if (entry.Status == "recording")
                Assert.Matches(new Regex("^(GET|POST|PUT|DELETE|PATCH) /"), entry.Route ?? "");
            else
                Assert.False(string.IsNullOrWhiteSpace(entry.Describe), $"{entry.Kind} is wanted but has no describe");

            if (entry.Shows == "branch")
                Assert.Contains(entry.BranchFrom, kinds);
        }
    }

    [Fact]
    public void Register_RecordingRoutesAreRealEndpoints()
    {
        using var factory = new DotWatcherApiFactory();
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m} {ActivityRegister.NormalizeRoute(e.RoutePattern.RawText ?? "")}"))
            .ToHashSet();

        foreach (var entry in ActivityRegister.Events.Where(e => e.Status == "recording"))
            Assert.True(endpoints.Contains(entry.Route!), $"{entry.Kind}: no endpoint for \"{entry.Route}\"");
    }

    [Fact]
    public async Task RegisterEndpoint_ListsEveryEntry()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Get, "/admin/activity/register", AdminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(ActivityRegister.Events.Count, entries.Count);
        var watched = entries.Single(e => e.GetProperty("kind").GetString() == "watch_link_opened");
        Assert.Equal("branch", watched.GetProperty("shows").GetString());
        Assert.Equal("watch_link_made", watched.GetProperty("branchFrom").GetString());
    }

    // ---- Recording a Go run --------------------------------------------------------------------

    [Fact]
    public async Task GoRun_RecordsStartWatchLinkAndStop_OnTheRunnersLine()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        await StartAndPostAsync(client, alice.AccessToken);
        await CreateLinkAsync(client, alice.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken)).StatusCode);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "run_stopped"));

        Assert.Equal(new[] { "run_started", "watch_link_made", "run_stopped" }, events.Select(e => e.Kind).ToArray());
        var runId = events[0].RunId;
        Assert.False(string.IsNullOrEmpty(runId));
        Assert.All(events, e => Assert.Equal(runId, e.RunId));
        Assert.All(events, e => Assert.Equal(alice.User.UserId, e.UserId));
        Assert.True(events[2].Detail!.Value.GetProperty("durationSeconds").GetInt32() >= 0);
    }

    [Fact]
    public async Task StartingAgain_ToContinueTheRun_RecordsOneStart()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        await StartAndPostAsync(client, alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "run_stopped"));

        Assert.Single(events, e => e.Kind == "run_started");
    }

    [Fact]
    public async Task NothingHappening_RecordsNothing()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        // Not out: the watch link is refused (409) and the stop is a no-op (204).
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, "/casual/watch-link", alice.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken)).StatusCode);
        await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "run_started"));

        Assert.Equal(new[] { "run_started" }, events.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public async Task WatchLinkOpens_GoOnTheOwnersLine_OncePerBrowser()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        // The watch page polls every 10 s; two browsers, several polls each.
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await WatchAsync(client, link.Path, "browser-one")).StatusCode);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await WatchAsync(client, link.Path, "browser-two")).StatusCode);
        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "run_stopped"));

        var opens = events.Where(e => e.Kind == "watch_link_opened").ToList();
        Assert.Equal(2, opens.Count);
        Assert.All(opens, e => Assert.Equal(alice.User.UserId, e.UserId));
        Assert.All(opens, e => Assert.Equal(events[0].RunId, e.RunId));
    }

    [Fact]
    public async Task UnknownWatchLink_RecordsNothing()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, (await WatchAsync(client, "/watch/AAAAAAAAAAAAAAAAAAAAAA", "browser-one")).StatusCode);
        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "run_stopped"));

        Assert.DoesNotContain(events, e => e.Kind == "watch_link_opened");
    }

    // ---- The admin endpoints -------------------------------------------------------------------

    [Fact]
    public async Task Dots_ListsWhoWasActive()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartAndPostAsync(client, alice.AccessToken);
        await WaitForEventsAsync(client, alice.User.UserId, e => e.Count > 0);

        var (from, to) = Window();
        var response = await SendAsync(client, HttpMethod.Get, $"/admin/activity/dots?from={Q(from)}&to={Q(to)}", AdminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dots = (await response.Content.ReadFromJsonAsync<List<ActivityDot>>())!;
        var dot = Assert.Single(dots);
        Assert.Equal(alice.User.UserId, dot.UserId);
        Assert.Equal("AL", dot.DisplayName);
        Assert.Equal(1, dot.EventCount);
        Assert.DoesNotContain(dots, d => d.UserId == bob.User.UserId);
    }

    [Fact]
    public async Task AdminEndpoints_RefuseAUserToken()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var (from, to) = Window();

        foreach (var url in new[]
                 {
                     $"/admin/activity/dots?from={Q(from)}&to={Q(to)}",
                     $"/admin/activity?from={Q(from)}&to={Q(to)}&userIds={alice.User.UserId}",
                     "/admin/activity/register",
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, url, alice.AccessToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Events_NeedARangeAndUsers()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (from, to) = Window();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, "/admin/activity?userIds=x", AdminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, $"/admin/activity?from={Q(from)}&to={Q(to)}", AdminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Get, $"/admin/activity?from={Q(to)}&to={Q(from)}&userIds=x", AdminToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Get, $"/admin/activity?from={Q(from.AddDays(-30))}&to={Q(to)}&userIds=x", AdminToken)).StatusCode);
    }

    // ---- Keeping it --------------------------------------------------------------------------

    [Fact]
    public async Task DeletingTheAccount_DeletesItsEvents()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        await WaitForEventsAsync(client, alice.User.UserId, e => e.Count > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, "/me", alice.AccessToken)).StatusCode);

        Assert.Empty(await GetEventsAsync(client, alice.User.UserId));
    }

    [Fact]
    public async Task Purge_DeletesEventsPastRetention()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        await WaitForEventsAsync(client, alice.User.UserId, e => e.Count > 0);
        var store = factory.Services.GetRequiredService<SessionStore>();

        Assert.Equal(0, store.PurgeActivityEvents(DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal(1, store.PurgeActivityEvents(DateTimeOffset.UtcNow.AddMinutes(5)));

        Assert.Empty(await GetEventsAsync(client, alice.User.UserId));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static (DateTimeOffset From, DateTimeOffset To) Window() =>
        (DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));

    private static string Q(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    private static async Task<List<ActivityEventView>> GetEventsAsync(HttpClient client, string userId)
    {
        var (from, to) = Window();
        var response = await SendAsync(client, HttpMethod.Get, $"/admin/activity?from={Q(from)}&to={Q(to)}&userIds={userId}", AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ActivityEventView>>())!;
    }

    // Events are written as the request finishes, which can be just after the client has its response.
    private static async Task<List<ActivityEventView>> WaitForEventsAsync(
        HttpClient client, string userId, Func<List<ActivityEventView>, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var events = await GetEventsAsync(client, userId);
            if (done(events) || DateTime.UtcNow > deadline)
                return events;
            await Task.Delay(50);
        }
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username, string displayName) =>
        await AuthTestHelpers.RegisterWithResponseAsync(client, username, displayName);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> WatchAsync(HttpClient client, string path, string browserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Browser-Id", browserId);
        return await client.SendAsync(request);
    }

    private static async Task StartAndPostAsync(HttpClient client, string accessToken)
    {
        var start = await SendAsync(client, HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate("ignored", SessionStore.CasualSessionId, Latitude, Longitude, 90, DateTimeOffset.UtcNow),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    private static async Task<CasualWatchLinkResponse> CreateLinkAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/casual/watch-link", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualWatchLinkResponse>())!;
    }
}
