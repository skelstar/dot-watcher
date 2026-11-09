using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Bot-dots, the server side: reserved accounts, the spawn-cells endpoint, the flag on dots, and the short
// retention of their positions. See .ai/plans/bot-dot-army.md.
public class BotDotApiTests
{
    private const double HereLatitude = -41.17;
    private const double HereLongitude = 174.7762;
    // About 1.1 km north of Here: inside the 5 km radius, outside the 300 m start fuzz.
    private const double NearLatitude = -41.16;

    private static DotWatcherApiFactory BotFactory(bool enabled = true) =>
        new() { EnabledForAll = [Features.Crew, Features.Casual], BotDotsEnabled = enabled };

    // ---- registering -----------------------------------------------------------------------------

    [Theory]
    [InlineData("botdot-001")]
    [InlineData("BotDot-002")]
    public async Task Register_ReservedUsername_WithoutTheAdminBearer_IsRefused(string username)
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, username, adminBearer: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ReservedUsername_WithTheAdminBearer_WorksWhileBotDotsAreOn()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, "botdot-001", adminBearer: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_ReservedUsername_IsRefusedEvenWithTheAdminBearer_WhileBotDotsAreOff()
    {
        using var factory = BotFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, "botdot-001", adminBearer: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_OrdinaryUsernames_AreUnaffected()
    {
        using var factory = BotFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, "robot-fan", adminBearer: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- cells -----------------------------------------------------------------------------------

    [Fact]
    public async Task Cells_RequireTheAdminBearer()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        var alice = await RunnerAsync(client, "alice", "AL", visible: true);

        using var noAuth = new HttpRequestMessage(HttpMethod.Get, "/admin/casual/cells");
        using var userAuth = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/admin/casual/cells", alice.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(noAuth)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(userAuth)).StatusCode);
    }

    [Fact]
    public async Task Cells_AreNotFound_WhileBotDotsAreOff()
    {
        using var factory = BotFactory(enabled: false);
        using var client = factory.CreateClient();

        var response = await GetCellsAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cells_ListACellWithARealRunnerWhoIsVisibleToPeopleNearby_AndNothingElse()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        await RunnerAsync(client, "alice", "AL", visible: true);

        var response = await GetCellsAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cell = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(GeoHash.Encode(NearLatitude, HereLongitude, BotDots.CellPrecision), cell.GetProperty("cell").GetString());
        Assert.Equal(1, cell.GetProperty("realCount").GetInt32());
        Assert.Equal(0, cell.GetProperty("botDotCount").GetInt32());

        // Coarse by design: a cell and counts, never a person or an exact position.
        Assert.Equal(
            new[] { "cell", "latitude", "longitude", "realCount", "botDotCount" },
            cell.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        var centre = GeoHash.DecodeCentre(cell.GetProperty("cell").GetString())!.Value;
        Assert.Equal(centre.Latitude, cell.GetProperty("latitude").GetDouble(), 9);
        Assert.NotEqual(NearLatitude, cell.GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task Cells_LeaveOutARunnerWhoIsNotVisibleToPeopleNearby()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        await RunnerAsync(client, "alice", "AL", visible: false);

        var response = await GetCellsAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Cells_CountBotDotsSeparately_FromRealRunners()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        await RunnerAsync(client, "alice", "AL", visible: true);
        await BotDotAsync(client, "botdot-001");

        var response = await GetCellsAsync(client);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cell = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(1, cell.GetProperty("realCount").GetInt32());
        Assert.Equal(1, cell.GetProperty("botDotCount").GetInt32());
    }

    // ---- what people see -------------------------------------------------------------------------

    [Fact]
    public async Task ABotDot_ShowsToAnOptedInRunner_AsAFlaggedGreyDot()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        var alice = await RunnerAsync(client, "alice", "AL", visible: true);
        await BotDotAsync(client, "botdot-001");

        var live = await GetLiveRawAsync(client, alice.AccessToken);

        var dot = Assert.Single(live.RootElement.GetProperty("nearby").EnumerateArray());
        Assert.True(dot.GetProperty("isBot").GetBoolean());
        // No name, id or timestamp: a bot-dot is as anonymous as a stranger.
        Assert.Equal(
            new[] { "heading", "inSession", "isBot", "latitude", "longitude" },
            dot.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ABotDot_IsHidden_FromARunnerWhoHasNotOptedInToNearbyDots()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        var alice = await RunnerAsync(client, "alice", "AL", visible: false);
        await BotDotAsync(client, "botdot-001");

        var live = await GetLiveRawAsync(client, alice.AccessToken);

        Assert.Empty(live.RootElement.GetProperty("nearby").EnumerateArray());
    }

    [Fact]
    public async Task APerson_IsNotFlagged_AndTheFieldIsAbsentFromTheJson()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        var alice = await RunnerAsync(client, "alice", "AL", visible: true);
        await RunnerAsync(client, "bob", "BO", visible: true);

        var live = await GetLiveRawAsync(client, alice.AccessToken);

        var dot = Assert.Single(live.RootElement.GetProperty("nearby").EnumerateArray());
        Assert.False(dot.TryGetProperty("isBot", out _));
    }

    [Fact]
    public async Task BotDots_AreNotDrawn_OnceTheyAreSwitchedOff()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        var store = (SessionStore)factory.Services.GetService(typeof(SessionStore))!;
        var alice = await RunnerAsync(client, "alice", "AL", visible: true);
        await BotDotAsync(client, "botdot-001");
        Assert.Single((await GetLiveRawAsync(client, alice.AccessToken)).RootElement.GetProperty("nearby").EnumerateArray());

        store.BotDotsEnabled = false;

        Assert.Empty((await GetLiveRawAsync(client, alice.AccessToken)).RootElement.GetProperty("nearby").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await GetCellsAsync(client)).StatusCode);
    }

    // ---- accounts and retention ------------------------------------------------------------------

    [Fact]
    public async Task AdminUserList_MarksBotDotAccounts()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        await RunnerAsync(client, "alice", "AL", visible: true);
        await BotDotAsync(client, "botdot-001");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var users = json.RootElement.EnumerateArray().ToDictionary(
            u => u.GetProperty("username").GetString()!, u => u.GetProperty("isBotDot").GetBoolean());
        Assert.True(users["botdot-001"]);
        Assert.False(users["alice"]);
    }

    [Fact]
    public async Task Maintenance_DeletesBotDotPositionsAfterAnHour_ButNotAPersons()
    {
        using var factory = BotFactory();
        using var client = factory.CreateClient();
        var store = (SessionStore)factory.Services.GetService(typeof(SessionStore))!;
        await RunnerAsync(client, "alice", "AL", visible: true);
        await BotDotAsync(client, "botdot-001");

        // Without a bot-dot retention nothing is touched (a person's and a bot-dot's are both inside 7 days).
        var twoHoursOn = DateTimeOffset.UtcNow.AddHours(2);
        Assert.Equal(0, store.PurgeCasualData(twoHoursOn, TimeSpan.FromDays(7)).DeletedPositions);

        // Two hours on, only the bot-dot's two positions go; the person's two stay.
        var (_, deleted, _) = store.PurgeCasualData(twoHoursOn, TimeSpan.FromDays(7), BotDots.PositionRetention);
        Assert.Equal(2, deleted);
        var (_, later, _) = store.PurgeCasualData(DateTimeOffset.UtcNow.AddDays(8), TimeSpan.FromDays(7), BotDots.PositionRetention);
        Assert.Equal(2, later);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string username, bool adminBearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/register")
        {
            Content = JsonContent.Create(new { username, password = "correct-horse-password", displayName = "XX" }),
        };
        if (adminBearer)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetCellsAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/casual/cells");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client.SendAsync(request);
    }

    // A real person, out in casual: a start 1.1 km south of NearLatitude then a position at NearLatitude, so
    // they are past the 300 m start fuzz.
    private static async Task<AuthResponse> RunnerAsync(HttpClient client, string username, string initials, bool visible)
    {
        var auth = await AuthTestHelpers.RegisterWithResponseAsync(client, username, initials);
        await StartOutAsync(client, auth.AccessToken, visible);
        return auth;
    }

    // A bot-dot, made the way the service makes one: registered with the admin bearer, then an ordinary casual runner.
    private static async Task BotDotAsync(HttpClient client, string username)
    {
        var response = await RegisterAsync(client, username, adminBearer: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        await StartOutAsync(client, auth.AccessToken, visible: true);
    }

    private static async Task StartOutAsync(HttpClient client, string accessToken, bool visible)
    {
        using (var settings = AuthTestHelpers.WithUserToken(HttpMethod.Put, "/me/casual/settings", accessToken))
        {
            settings.Content = JsonContent.Create(new { visibleToNonCrew = visible });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(settings)).StatusCode);
        }

        using (var start = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/start", accessToken))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(start)).StatusCode);

        await PostAsync(client, accessToken, HereLatitude, secondsAgo: 20);
        await PostAsync(client, accessToken, NearLatitude, secondsAgo: 0);
    }

    private static async Task PostAsync(HttpClient client, string accessToken, double latitude, int secondsAgo)
    {
        var update = new LocationUpdate(
            RunnerName: "ignored",
            SessionId: SessionStore.CasualSessionId,
            Latitude: latitude,
            Longitude: HereLongitude,
            Heading: 90,
            Timestamp: DateTimeOffset.UtcNow.AddSeconds(-secondsAgo));
        var response = await LocationsApiTests.PostLocationAsync(client, update, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonDocument> GetLiveRawAsync(HttpClient client, string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/casual/live", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
