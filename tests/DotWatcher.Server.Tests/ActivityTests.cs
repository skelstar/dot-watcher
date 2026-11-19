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
    public async Task AWatchViewing_GetsItsLength_OnceItGoesQuiet()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);
        for (var i = 0; i < 3; i++)
            await WatchAsync(client, link.Path, "browser-one");
        await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "watch_link_opened"));

        // As if a minute passed with no poll: the tab was hidden or closed.
        var tracker = factory.Services.GetRequiredService<WatchViewingTracker>();
        Assert.Equal(1, tracker.Sweep(DateTimeOffset.UtcNow.AddMinutes(1)));

        var opened = (await GetEventsAsync(client, alice.User.UserId)).Single(e => e.Kind == "watch_link_opened");
        Assert.True(opened.Detail!.Value.GetProperty("watchedSeconds").GetInt32() >= 0);
    }

    [Fact]
    public void AGapInPolls_StartsANewViewing()
    {
        using var factory = new DotWatcherApiFactory();
        _ = factory.CreateClient();
        var tracker = factory.Services.GetRequiredService<WatchViewingTracker>();
        var start = DateTimeOffset.UtcNow;

        var first = tracker.Poll("run-1", "viewer", start);
        Assert.NotNull(first);
        Assert.Null(tracker.Poll("run-1", "viewer", start.AddSeconds(10)));
        Assert.Null(tracker.Poll("run-1", "viewer", start.AddSeconds(20)));
        // Hidden for longer than the gap, then back.
        var second = tracker.Poll("run-1", "viewer", start.AddSeconds(20) + tracker.Gap + TimeSpan.FromSeconds(1));
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        // Another viewer, or another run, is a viewing of its own.
        Assert.NotNull(tracker.Poll("run-1", "someone-else", start.AddSeconds(30)));
        Assert.NotNull(tracker.Poll("run-2", "viewer", start.AddSeconds(30)));
    }

    [Fact]
    public async Task WatchingAfterTheRunEnded_RecordsNothing()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await WatchAsync(client, link.Path, "browser-late")).StatusCode);
        await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Count(x => x.Kind == "run_started") == 2);

        Assert.DoesNotContain(events, e => e.Kind == "watch_link_opened");
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

    // ---- What the app reports itself -----------------------------------------------------------

    [Fact]
    public async Task AppReports_OpenedAndBackgrounded_AreRecorded_AndAQuickRepeatIsDropped()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        foreach (var kind in new[] { "app_opened", "app_opened", "app_backgrounded", "app_opened" })
            Assert.Equal(HttpStatusCode.NoContent,
                (await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { kind })).StatusCode);

        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Count >= 3);

        // The second "opened" repeats the one before it within a minute; away and back again both count.
        Assert.Equal(new[] { "app_opened", "app_backgrounded", "app_opened" }, events.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public async Task AppReports_NeedASignIn_AndAKindTheAppMaySend()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/me/activity", new { kind = "app_opened" })).StatusCode);
        foreach (var kind in new[] { "run_started", "made_up", "" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { kind })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { })).StatusCode);

        await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { kind = "app_opened" });
        var events = await WaitForEventsAsync(client, alice.User.UserId, e => e.Count > 0);

        Assert.Equal(new[] { "app_opened" }, events.Select(e => e.Kind).ToArray());
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

    // ---- Marking testing ------------------------------------------------------------------------

    [Fact]
    public async Task MarkingARunAsTest_HidesItAndWhatHappenedDuringIt_UntilAskedFor()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { kind = "app_opened" });
        await StartAndPostAsync(client, alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { kind = "app_backgrounded" });
        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);
        var all = await WaitForEventsAsync(client, alice.User.UserId, e => e.Any(x => x.Kind == "run_stopped"));
        var runId = all.Single(e => e.Kind == "run_started").RunId;

        var mark = await SendAsync(client, HttpMethod.Post, "/admin/activity/test", AdminToken,
            new { userId = alice.User.UserId, runId });

        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        // Start, the app being put away during the run (no run id of its own), and stop.
        Assert.Equal(3, (await mark.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetInt32());
        Assert.Equal(new[] { "app_opened" }, (await GetEventsAsync(client, alice.User.UserId)).Select(e => e.Kind).ToArray());

        var withTest = await GetEventsAsync(client, alice.User.UserId, includeTest: true);
        Assert.Equal(4, withTest.Count);
        Assert.Equal(new[] { false, true, true, true }, withTest.Select(e => e.IsTest).ToArray());
    }

    [Fact]
    public async Task MarkingAStretch_AndUnmarkingIt()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await SendAsync(client, HttpMethod.Post, "/me/activity", alice.AccessToken, new { kind = "app_opened" });
        await WaitForEventsAsync(client, alice.User.UserId, e => e.Count > 0);
        var (from, to) = Window();

        await SendAsync(client, HttpMethod.Post, "/admin/activity/test", AdminToken, new { userId = alice.User.UserId, from, to });
        Assert.Empty(await GetEventsAsync(client, alice.User.UserId));
        Assert.Empty(await GetDotsAsync(client));
        Assert.Single(await GetDotsAsync(client, includeTest: true));

        await SendAsync(client, HttpMethod.Post, "/admin/activity/test", AdminToken,
            new { userId = alice.User.UserId, from, to, isTest = false });
        Assert.Single(await GetEventsAsync(client, alice.User.UserId));
    }

    [Fact]
    public async Task MarkingNeedsAnAdmin_AndARunOrARange()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var (from, to) = Window();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, "/admin/activity/test", alice.AccessToken,
            new { userId = alice.User.UserId, from, to })).StatusCode);
        foreach (var body in new object[]
                 {
                     new { from, to },
                     new { userId = alice.User.UserId },
                     new { userId = alice.User.UserId, runId = "r", from, to },
                     new { userId = alice.User.UserId, from = to, to = from },
                 })
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/admin/activity/test", AdminToken, body)).StatusCode);
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

    private static async Task<List<ActivityEventView>> GetEventsAsync(HttpClient client, string userId, bool includeTest = false)
    {
        var (from, to) = Window();
        var response = await SendAsync(client, HttpMethod.Get,
            $"/admin/activity?from={Q(from)}&to={Q(to)}&userIds={userId}&includeTest={includeTest}", AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ActivityEventView>>())!;
    }

    private static async Task<List<ActivityDot>> GetDotsAsync(HttpClient client, bool includeTest = false)
    {
        var (from, to) = Window();
        var response = await SendAsync(client, HttpMethod.Get, $"/admin/activity/dots?from={Q(from)}&to={Q(to)}&includeTest={includeTest}", AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ActivityDot>>())!;
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
