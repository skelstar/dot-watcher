using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Groups entry codes (see .ai/plans/go-and-groups.md, step 2): an organiser can ask for a code to
// join a nearby session, so someone passing by sees the tile but can't join without it.
public class EntryCodeTests
{
    private const double Lat = -36.8500;
    private const double Lon = 174.7600;
    private const double About100m = 0.0009;

    private static DotWatcherApiFactory GroupsFactory() => new() { EnabledForAll = ["events", "groups"] };

    // ---- the pure rules --------------------------------------------------------------------------

    [Theory]
    [InlineData("K7Q4", "K7Q4")]
    [InlineData(" k7q4 ", "K7Q4")]
    [InlineData("HUNUA50K", "HUNUA50K")]
    public void Normalize_UpperCasesAndTrims(string given, string expected) =>
        Assert.Equal(expected, EntryCode.Normalize(given));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K7Q")]
    [InlineData("HUNUA50KM")]
    [InlineData("K7-Q4")]
    [InlineData("K7 Q4")]
    [InlineData("KÖQ4")]
    public void Normalize_RefusesAnythingElse(string? given) =>
        Assert.Null(EntryCode.Normalize(given));

    [Fact]
    public void Matches_IgnoresCaseAndSpaces_AndNoCodeAcceptsAnything()
    {
        Assert.True(EntryCode.Matches("K7Q4", " k7q4"));
        Assert.False(EntryCode.Matches("K7Q4", "K7Q1"));
        Assert.False(EntryCode.Matches("K7Q4", null));
        Assert.True(EntryCode.Matches(null, null));
        Assert.True(EntryCode.Matches(null, "ANYTHING"));
    }

    // ---- creating and managing -------------------------------------------------------------------

    [Fact]
    public async Task Create_WithACode_TheOwnerCanReadIt_NobodyElseCan()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var other = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "k7q4");

        var mine = await SendAsync(client, HttpMethod.Get, $"/sessions/{session.SessionId}/entry-code", organiser);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("K7Q4", (await mine.Content.ReadFromJsonAsync<EntryCodeStatus>())!.EntryCode);

        var theirs = await SendAsync(client, HttpMethod.Get, $"/sessions/{session.SessionId}/entry-code", other);
        Assert.Equal(HttpStatusCode.Forbidden, theirs.StatusCode);
    }

    [Fact]
    public async Task Create_WithACode_IsRefused_WithoutTheGroupsFeature()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        var response = await CreateNearbyRawAsync(client, organiser, entryCode: "K7Q4");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithACode_NeedsNearbyAndAValidCode()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        var notNearby = await SendAsync(client, HttpMethod.Post, "/sessions", organiser, new { entryCode = "K7Q4" });
        Assert.Equal(HttpStatusCode.BadRequest, notNearby.StatusCode);

        var tooShort = await CreateNearbyRawAsync(client, organiser, entryCode: "K7");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
    }

    [Fact]
    public async Task EntryCodeEndpoints_LookAbsent_WithoutTheGroupsFeature()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var session = await CreateNearbyAsync(client, organiser);

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Get, $"/sessions/{session.SessionId}/entry-code", organiser)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/entry-code", organiser, new { entryCode = "K7Q4" })).StatusCode);
    }

    [Fact]
    public async Task ChangingTheCode_KeepsPeopleWhoAreIn_AndOnlyTheNewCodeWorks()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var early = await AuthTestHelpers.RegisterAsync(client, "aroha", "AK");
        var late = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(client, early, session.SessionId, "K7Q4")).StatusCode);

        var change = await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/entry-code", organiser, new { entryCode = "new1" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal("NEW1", (await change.Content.ReadFromJsonAsync<EntryCodeStatus>())!.EntryCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await JoinAsync(client, late, session.SessionId, "K7Q4")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(client, late, session.SessionId, "NEW1")).StatusCode);
        Assert.True(await IsMemberAsync(client, early, session.SessionId));
    }

    [Fact]
    public async Task ClearingTheCode_LetsAnyoneNearbyJoin()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");

        var clear = await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/entry-code", organiser, new { entryCode = (string?)null });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(client, joiner, session.SessionId, entryCode: null)).StatusCode);
    }

    [Fact]
    public async Task ChangingTheCode_IsOwnerOnly_AndNotAfterTheEnd()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var other = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/entry-code", other, new { entryCode = "MINE" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/entry-code", organiser, new { entryCode = "K7" })).StatusCode);

        await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", organiser);
        Assert.Equal(HttpStatusCode.Gone,
            (await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/entry-code", organiser, new { entryCode = "NEW1" })).StatusCode);
    }

    // ---- the nearby list ---------------------------------------------------------------------------

    [Fact]
    public async Task List_SaysACodeIsNeeded_ToGroupsAccounts()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");

        var item = Assert.Single((await ListAsync(client, joiner)).EnumerateArray());

        Assert.True(item.GetProperty("needsCode").GetBoolean());
    }

    [Fact]
    public async Task List_LeavesOutCodedSessions_ForAccountsWithoutGroups_AndKeepsTheirShape()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        await GrantGroupsAsync(client, organiser.User.UserId);
        var coded = await CreateNearbyAsync(client, organiser.AccessToken, entryCode: "K7Q4", title: "Hunua 50k");
        var otherOrganiser = await AuthTestHelpers.RegisterAsync(client, "sean", "SE");
        var open = await CreateNearbyAsync(client, otherOrganiser, title: "Saturday long run");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");

        var items = (await ListAsync(client, joiner)).EnumerateArray().ToList();

        var item = Assert.Single(items);
        Assert.Equal(open.SessionId, item.GetProperty("sessionId").GetString());
        Assert.False(item.TryGetProperty("needsCode", out _));
        Assert.DoesNotContain(items, i => i.GetProperty("sessionId").GetString() == coded.SessionId);
    }

    // ---- joining -----------------------------------------------------------------------------------

    [Fact]
    public async Task Join_WithoutOrWithTheWrongCode_IsRefusedAsWrongCode_AndAddsNobody()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");

        foreach (var code in new[] { null, "K7Q1" })
        {
            var response = await JoinAsync(client, joiner, session.SessionId, code);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("entry_code", json.RootElement.GetProperty("reason").GetString());
        }

        Assert.False(await IsMemberAsync(client, joiner, session.SessionId));
    }

    [Fact]
    public async Task Join_WithTheCode_InAnyCase_Works()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");

        var response = await JoinAsync(client, joiner, session.SessionId, " k7q4 ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await IsMemberAsync(client, joiner, session.SessionId));
    }

    [Fact]
    public async Task Join_FromOutsideTheArea_IsNotAvailable_EvenWithTheRightCode_AndNeverSaysWrongCode()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, entryCode: "K7Q4");

        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(client, joiner, session.SessionId, "K7Q4", lat: Lat + 10 * About100m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(client, joiner, session.SessionId, "NOPE", lat: Lat + 10 * About100m)).StatusCode);
    }

    [Fact]
    public async Task Join_WithoutACodeSet_IgnoresAnyCodeSent()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser);

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(client, joiner, session.SessionId, "WHATEVER")).StatusCode);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static async Task<SessionMembership> CreateNearbyAsync(
        HttpClient client, string token, string? entryCode = null, string title = "Hunua 50k")
    {
        var response = await CreateNearbyRawAsync(client, token, entryCode, title);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static Task<HttpResponseMessage> CreateNearbyRawAsync(
        HttpClient client, string token, string? entryCode = null, string title = "Hunua 50k") =>
        SendAsync(client, HttpMethod.Post, "/sessions", token, new
        {
            sessionName = title,
            nearby = true,
            latitude = Lat,
            longitude = Lon,
            accuracy = 8,
            entryCode,
        });

    private static Task<HttpResponseMessage> JoinAsync(
        HttpClient client, string token, string sessionId, string? entryCode, double lat = Lat) =>
        SendAsync(client, HttpMethod.Post, $"/nearby/sessions/{sessionId}/join", token,
            new { latitude = lat, longitude = Lon, accuracy = 5, entryCode });

    private static async Task<JsonElement> ListAsync(HttpClient client, string token)
    {
        var query = FormattableString.Invariant($"lat={Lat + 0.5 * About100m}&lon={Lon}&accuracy=5");
        var response = await SendAsync(client, HttpMethod.Get, $"/me/events/nearby?{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<bool> IsMemberAsync(HttpClient client, string token, string sessionId)
    {
        var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", token);
        var sessions = await mine.Content.ReadFromJsonAsync<List<SessionMembership>>() ?? [];
        return sessions.Any(s => s.SessionId == sessionId);
    }

    private static async Task GrantGroupsAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/admin/users/{userId}/features")
        {
            Content = JsonContent.Create(new { features = new[] { "groups" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        Assert.True((await client.SendAsync(request)).IsSuccessStatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
