using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Casual tracking: one permanent session anyone with the feature can be out in. Crew see you by
// name, others nearby as anonymous dots. See .ai/plans/casual-tracking.md.
public class CasualApiTests
{
    // About 1.1 km north of Here: inside the 5 km radius, outside the 300 m start fuzz.
    private const double HereLatitude = -41.17;
    private const double HereLongitude = 174.7762;
    private const double NearLatitude = -41.16;
    // About 55 km north: far outside the 5 km radius.
    private const double FarLatitude = -40.67;

    private static DotWatcherApiFactory CasualFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual] };

    [Fact]
    public async Task Endpoints_WithoutTheFeature_LookAbsent()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Crew] };
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/casual/live", alice.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/me/casual/settings", alice.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutSignIn_AreUnauthorized()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/casual/start", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostToCasualSession_WithoutTheFeature_IsForbidden()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Crew] };
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var response = await PostCasualAsync(client, alice.AccessToken, HereLatitude);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostToCasualSession_BeforeStart_IsForbidden()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var response = await PostCasualAsync(client, alice.AccessToken, HereLatitude);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Start_IsIdempotent_AndReturnsTheCasualSession()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var first = await StartAsync(client, alice.AccessToken);
        var second = await StartAsync(client, alice.AccessToken);

        Assert.Equal(SessionStore.CasualSessionId, first.SessionId);
        Assert.Equal(first.RunId, second.RunId);
    }

    [Fact]
    public async Task Start_AfterStop_IsANewRun()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var first = await StartAsync(client, alice.AccessToken);
        await StopAsync(client, alice.AccessToken);
        var second = await StartAsync(client, alice.AccessToken);

        Assert.NotEqual(first.RunId, second.RunId);
    }

    [Fact]
    public async Task Post_AfterStop_IsForbidden_AndDoesNotBringTheRunnerBack()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await StartAsync(client, alice.AccessToken);
        await PostCasualAsync(client, alice.AccessToken, HereLatitude);
        await StopAsync(client, alice.AccessToken);

        var late = await PostCasualAsync(client, alice.AccessToken, HereLatitude);

        Assert.Equal(HttpStatusCode.Forbidden, late.StatusCode);
        Assert.Empty((await GetLiveAsync(client, bob.AccessToken)).Participants);
    }

    [Fact]
    public async Task Post_ReturnsTheRunnerThemselves()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAsync(client, alice.AccessToken);

        var live = await PostCasualOkAsync(client, alice.AccessToken, HereLatitude);

        Assert.Equal(new[] { "AL" }, live.Participants);
        var self = Assert.Single(live.Positions).Single();
        Assert.True(self.IsSelf);
        Assert.False(self.InSession);
        Assert.Empty(live.Nearby);
    }

    [Fact]
    public async Task Crew_SeeEachOtherByNicknameOrInitials_AtAnyDistance()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await SetNicknameAsync(client, bob.AccessToken, "Bobby");
        await StartAsync(client, alice.AccessToken);
        await StartAsync(client, bob.AccessToken);
        await PostCasualOkAsync(client, bob.AccessToken, FarLatitude);

        var live = await PostCasualOkAsync(client, alice.AccessToken, HereLatitude);

        Assert.Contains("AL", live.Participants);
        Assert.Contains("Bobby", live.Participants);
        Assert.Empty(live.Nearby);

        var bobSees = await PostCasualOkAsync(client, bob.AccessToken, FarLatitude);
        Assert.Contains("AL", bobSees.Participants);
    }

    [Fact]
    public async Task Crew_SeeARunner_EvenWithTheToggleOff()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await StartAsync(client, bob.AccessToken);
        await PostCasualOkAsync(client, bob.AccessToken, HereLatitude);

        var live = await GetLiveAsync(client, alice.AccessToken);

        Assert.Equal(new[] { "BO" }, live.Participants);
    }

    [Fact]
    public async Task Watcher_NotTracking_SeesCrewWithoutPostingAnything()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await StartAsync(client, bob.AccessToken);
        await PostCasualOkAsync(client, bob.AccessToken, HereLatitude);

        var live = await GetLiveAsync(client, alice.AccessToken, HereLatitude, HereLongitude);

        Assert.Equal(new[] { "BO" }, live.Participants);
        Assert.False(live.Positions.Single().Single().IsSelf);
    }

    [Fact]
    public async Task NonCrew_AreHidden_UntilBothHaveTheToggleOn()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartRunningAsync(client, bob.AccessToken);
        await StartAsync(client, alice.AccessToken);

        // Neither has opted in.
        Assert.Empty((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);

        // Bob opts in but Alice hasn't, so she sees no one (and he is hidden from her).
        await SetVisibleAsync(client, bob.AccessToken, true);
        Assert.Empty((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);

        // Alice opts in too.
        await SetVisibleAsync(client, alice.AccessToken, true);
        var live = await PostCasualOkAsync(client, alice.AccessToken, HereLatitude);
        var dot = Assert.Single(live.Nearby);
        Assert.Equal(NearLatitude, dot.Latitude);
        Assert.Equal(new[] { "AL" }, live.Participants);
    }

    [Fact]
    public async Task NonCrew_AreAnonymous_NoNameOrId()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartRunningAsync(client, bob.AccessToken);
        await SetVisibleAsync(client, bob.AccessToken, true);
        await SetVisibleAsync(client, alice.AccessToken, true);
        await StartAsync(client, alice.AccessToken);

        var response = await PostCasualAsync(client, alice.AccessToken, HereLatitude);
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("BO", json);
        Assert.DoesNotContain(bob.User.UserId, json);
        Assert.DoesNotContain("bob", json);
    }

    [Fact]
    public async Task NonCrew_BeyondFiveKilometres_AreHidden()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await SetVisibleAsync(client, bob.AccessToken, true);
        await SetVisibleAsync(client, alice.AccessToken, true);
        await StartAsync(client, bob.AccessToken);
        await PostCasualOkAsync(client, bob.AccessToken, FarLatitude, secondsAgo: 20);
        await PostCasualOkAsync(client, bob.AccessToken, FarLatitude + 0.02);
        await StartAsync(client, alice.AccessToken);

        var live = await PostCasualOkAsync(client, alice.AccessToken, HereLatitude);

        Assert.Empty(live.Nearby);
    }

    [Fact]
    public async Task NonCrew_WithinThreeHundredMetresOfTheirStart_AreHidden()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await SetVisibleAsync(client, bob.AccessToken, true);
        await SetVisibleAsync(client, alice.AccessToken, true);
        await StartAsync(client, bob.AccessToken);
        // Bob's start, then a point about 110 m away: still inside the fuzz.
        await PostCasualOkAsync(client, bob.AccessToken, NearLatitude, secondsAgo: 20);
        await PostCasualOkAsync(client, bob.AccessToken, NearLatitude + 0.001);
        await StartAsync(client, alice.AccessToken);

        Assert.Empty((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);

        // Once he has moved on, he shows.
        await PostCasualOkAsync(client, bob.AccessToken, NearLatitude + 0.01);
        Assert.Single((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);
    }

    [Fact]
    public async Task Stop_RemovesTheRunnerFromEveryonesMap()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await SetVisibleAsync(client, bob.AccessToken, true);
        await SetVisibleAsync(client, alice.AccessToken, true);
        await StartRunningAsync(client, bob.AccessToken);
        await StartAsync(client, alice.AccessToken);
        Assert.Single((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);

        await StopAsync(client, bob.AccessToken);

        Assert.Empty((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);
    }

    [Fact]
    public async Task Blocked_NeverAppear_EvenAsCrewOrDots()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await SetVisibleAsync(client, bob.AccessToken, true);
        await SetVisibleAsync(client, alice.AccessToken, true);
        await StartRunningAsync(client, bob.AccessToken);
        await StartAsync(client, alice.AccessToken);
        Assert.Single((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);

        var block = await SendAsync(client, HttpMethod.Post, $"/me/blocks/{bob.User.UserId}", alice.AccessToken);
        Assert.True(block.IsSuccessStatusCode);

        Assert.Empty((await PostCasualOkAsync(client, alice.AccessToken, HereLatitude)).Nearby);
    }

    [Fact]
    public async Task Settings_DefaultToOff_AndPersist()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        Assert.False((await GetSettingsAsync(client, alice.AccessToken)).VisibleToNonCrew);

        await SetVisibleAsync(client, alice.AccessToken, true);
        Assert.True((await GetSettingsAsync(client, alice.AccessToken)).VisibleToNonCrew);

        // An empty update leaves it alone.
        var untouched = await SendAsync(client, HttpMethod.Put, "/me/casual/settings", alice.AccessToken, new { });
        Assert.True((await untouched.Content.ReadFromJsonAsync<CasualSettings>())!.VisibleToNonCrew);
    }

    [Fact]
    public async Task NamedSession_RunnerWithToggleOn_AppearsInCasualWithTheLock()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await SetVisibleAsync(client, alice.AccessToken, true);
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice.AccessToken);

        var named = await LocationsApiTests.PostLocationAsync(client, Fix(session.SessionId, HereLatitude), alice.AccessToken);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);

        var live = await GetLiveAsync(client, bob.AccessToken);
        var position = Assert.Single(live.Positions).Single();
        Assert.Equal("AL", position.RunnerName);
        Assert.True(position.InSession);
    }

    [Fact]
    public async Task NamedSession_RunnerWithToggleOn_ShowsToNonCrewAsALockedDot()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await SetVisibleAsync(client, alice.AccessToken, true);
        await SetVisibleAsync(client, bob.AccessToken, true);
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice.AccessToken);
        await LocationsApiTests.PostLocationAsync(client, Fix(session.SessionId, NearLatitude, secondsAgo: 20), alice.AccessToken);
        await LocationsApiTests.PostLocationAsync(client, Fix(session.SessionId, NearLatitude + 0.01), alice.AccessToken);
        await StartAsync(client, bob.AccessToken);

        var live = await PostCasualOkAsync(client, bob.AccessToken, HereLatitude);

        Assert.True(Assert.Single(live.Nearby).InSession);
    }

    [Fact]
    public async Task NamedSession_RunnerWithToggleOff_IsNotCopiedToCasual()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice.AccessToken);

        await LocationsApiTests.PostLocationAsync(client, Fix(session.SessionId, HereLatitude), alice.AccessToken);

        Assert.Empty((await GetLiveAsync(client, bob.AccessToken)).Participants);
    }

    [Fact]
    public async Task NamedSession_ResponseShape_IsUnchanged()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await SetVisibleAsync(client, alice.AccessToken, true);
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice.AccessToken);

        var response = await LocationsApiTests.PostLocationAsync(client, Fix(session.SessionId, HereLatitude), alice.AccessToken);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("participants", json);
        Assert.Contains("positions", json);
        Assert.DoesNotContain("nearby", json);
        Assert.DoesNotContain("inSession", json);
    }

    [Fact]
    public async Task CasualSession_IsNotReachableThroughSessionEndpoints()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAsync(client, alice.AccessToken);
        await PostCasualOkAsync(client, alice.AccessToken, HereLatitude);

        var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", alice.AccessToken);
        Assert.DoesNotContain(SessionStore.CasualSessionId, await mine.Content.ReadAsStringAsync());

        var locations = await SendAsync(client, HttpMethod.Get, $"/locations/{SessionStore.CasualSessionId}", alice.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, locations.StatusCode);

        var runners = await SendAsync(client, HttpMethod.Get, $"/sessions/{SessionStore.CasualSessionId}/runners", alice.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, runners.StatusCode);
    }

    [Fact]
    public async Task CasualSession_CannotBeFoundByAnyInviteCode()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var store = (SessionStore)factory.Services.GetService(typeof(SessionStore))!;

        // The stored code is 32 hex characters; invite codes are 4-8, so no lookup can match it.
        var code = store.GetInviteCodeBySessionId(SessionStore.CasualSessionId)!;

        Assert.Equal(32, code.Length);
        Assert.Null(store.GetSessionIdByInviteCode(code));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/session-invites/{code}/locations")).StatusCode);
    }

    [Fact]
    public async Task DeletingAnAccount_RemovesItFromCasual()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await StartRunningAsync(client, bob.AccessToken);
        Assert.Single((await GetLiveAsync(client, alice.AccessToken)).Participants);

        var delete = await SendAsync(client, HttpMethod.Delete, "/me", bob.AccessToken);
        Assert.True(delete.IsSuccessStatusCode);

        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Participants);
    }

    [Fact]
    public async Task LiveWithOnlyOneCoordinate_IsABadRequest()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var response = await SendAsync(client, HttpMethod.Get, "/casual/live?lat=-41.17", alice.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_ClosesAbandonedRuns_AndDeletesOldPositions()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        await StartRunningAsync(client, bob.AccessToken);
        var store = (SessionStore)factory.Services.GetService(typeof(SessionStore))!;

        // Nothing is old yet.
        Assert.Equal((0, 0, 0), store.PurgeCasualData(DateTimeOffset.UtcNow, TimeSpan.FromDays(7)));

        // Eight days on: Bob's run is abandoned (closed), and its positions are past retention.
        var later = DateTimeOffset.UtcNow.AddDays(8);
        var (closed, positions, _) = store.PurgeCasualData(later, TimeSpan.FromDays(7));
        Assert.Equal(1, closed);
        Assert.Equal(2, positions);
        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Participants);
    }

    [Fact]
    public async Task Contract_StartAndLive_HaveExpectedJsonFields()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await SetVisibleAsync(client, alice.AccessToken, true);
        await SetVisibleAsync(client, bob.AccessToken, true);
        await StartRunningAsync(client, bob.AccessToken);

        var start = await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken);
        using var startJson = System.Text.Json.JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        Assert.Equal(System.Text.Json.JsonValueKind.String, startJson.RootElement.GetProperty("sessionId").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.String, startJson.RootElement.GetProperty("runId").ValueKind);

        var live = await PostCasualAsync(client, alice.AccessToken, HereLatitude);
        using var liveJson = System.Text.Json.JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        var root = liveJson.RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Array, root.GetProperty("participants").ValueKind);

        // Same fields as RunnerPosition, so a client can decode it as one, plus the two additions.
        var position = root.GetProperty("positions")[0][0];
        foreach (var field in new[] { "runnerName", "latitude", "longitude", "heading", "timestamp", "nextExpectedAt", "isUltraConstrained", "batteryLevel", "inSession", "isSelf" })
            Assert.True(position.TryGetProperty(field, out _), field);

        var dot = root.GetProperty("nearby")[0];
        Assert.Equal(
            new[] { "heading", "inSession", "latitude", "longitude" },
            dot.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username, string displayName) =>
        await AuthTestHelpers.RegisterWithResponseAsync(client, username, displayName);

    private static async Task<(AuthResponse Owner, AuthResponse Joiner)> MakeCrewAsync(
        HttpClient client, string ownerUsername, string joinerUsername)
    {
        var owner = await RegisterAsync(client, ownerUsername, ownerUsername[..2].ToUpperInvariant());
        var joiner = await RegisterAsync(client, joinerUsername, joinerUsername[..2].ToUpperInvariant());
        var code = await CrewApiTests.CreateCodeAsync(client, owner.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", joiner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        return (owner, joiner);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<CasualStartResponse> StartAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualStartResponse>())!;
    }

    private static async Task StopAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/casual/stop", accessToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Starts a run and posts a first position (the run's start) then a second about 1.1 km on, so
    // the runner is past the 300 m start fuzz and visible to non-crew.
    private static async Task StartRunningAsync(HttpClient client, string accessToken)
    {
        await StartAsync(client, accessToken);
        await PostCasualOkAsync(client, accessToken, NearLatitude - 0.01, secondsAgo: 20);
        await PostCasualOkAsync(client, accessToken, NearLatitude);
    }

    private static async Task SetVisibleAsync(HttpClient client, string accessToken, bool visible)
    {
        var response = await SendAsync(client, HttpMethod.Put, "/me/casual/settings", accessToken, new { visibleToNonCrew = visible });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<CasualSettings> GetSettingsAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/casual/settings", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualSettings>())!;
    }

    private static async Task SetNicknameAsync(HttpClient client, string accessToken, string nickname)
    {
        var response = await SendAsync(client, HttpMethod.Put, "/me/profile", accessToken, new { nickname });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostCasualAsync(
        HttpClient client, string accessToken, double latitude, int secondsAgo = 0) =>
        LocationsApiTests.PostLocationAsync(client, Fix(SessionStore.CasualSessionId, latitude, secondsAgo: secondsAgo), accessToken);

    private static async Task<CasualLiveResponse> PostCasualOkAsync(
        HttpClient client, string accessToken, double latitude, int secondsAgo = 0)
    {
        var response = await PostCasualAsync(client, accessToken, latitude, secondsAgo);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualLiveResponse>())!;
    }

    private static async Task<CasualLiveResponse> GetLiveAsync(
        HttpClient client, string accessToken, double? lat = null, double? lon = null)
    {
        var url = lat is null ? "/casual/live" : $"/casual/live?lat={lat}&lon={lon}";
        var response = await SendAsync(client, HttpMethod.Get, url, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualLiveResponse>())!;
    }

    private static LocationUpdate Fix(string sessionId, double latitude, int secondsAgo = 0) =>
        new(
            RunnerName: "ignored",
            SessionId: sessionId,
            Latitude: latitude,
            Longitude: HereLongitude,
            Heading: 90,
            Timestamp: DateTimeOffset.UtcNow.AddSeconds(-secondsAgo));
}
