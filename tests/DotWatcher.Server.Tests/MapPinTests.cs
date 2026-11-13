using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Message pins on the map: in a named session (everyone in it sees them; the author or owner deletes)
// and on a casual run (the runner, their crew and watch links see them; gone when the run ends).
// See .ai/plans/session-map-pins.md.
public class MapPinTests
{
    private const double Lat = -41.17;
    private const double Lon = 174.7762;

    private static DotWatcherApiFactory CasualFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual] };

    // ---- Named sessions ------------------------------------------------------------------------

    [Fact]
    public async Task SessionPin_IsSeenByEveryoneInTheSession_LabelledWithTheAuthorsName()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var runner = await AuthTestHelpers.RegisterWithResponseAsync(client, "runner", "RU");
        var viewer = await AuthTestHelpers.RegisterWithResponseAsync(client, "viewer", "VI");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        await AuthTestHelpers.JoinSessionAsync(client, runner.AccessToken, session.InviteCode, role: "runner");
        await AuthTestHelpers.JoinSessionAsync(client, viewer.AccessToken, session.InviteCode);

        var created = await CreateSessionPinAsync(client, session.SessionId, runner.AccessToken, "Water here");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var pin = (await created.Content.ReadFromJsonAsync<MapMessagePin>())!;
        Assert.Equal("Water here", pin.Message);
        Assert.True(pin.IsMine);

        var seen = Assert.Single(await GetSessionPinsAsync(client, session.SessionId, viewer.AccessToken));
        Assert.Equal(pin.Id, seen.Id);
        Assert.Equal(pin.Author, seen.Author);
        Assert.False(seen.IsMine);
        Assert.False(seen.CanDelete);

        var ownerSees = Assert.Single(await GetSessionPinsAsync(client, session.SessionId, owner.AccessToken));
        Assert.True(ownerSees.CanDelete);
    }

    [Fact]
    public async Task SessionPin_ViewersCantDropOne_AndOutsidersCantSeeThem()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var viewer = await AuthTestHelpers.RegisterWithResponseAsync(client, "viewer", "VI");
        var outsider = await AuthTestHelpers.RegisterWithResponseAsync(client, "outsider", "OU");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        await AuthTestHelpers.JoinSessionAsync(client, viewer.AccessToken, session.InviteCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await CreateSessionPinAsync(client, session.SessionId, viewer.AccessToken, "Hi")).StatusCode);

        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, $"/sessions/{session.SessionId}/pins", outsider.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SessionPin_WithNoMessage_IsRejected(string? message)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);

        var response = await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SessionPin_TooLongOrOffTheMap_IsRejected()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, new string('a', MapPinRules.MaxMessageLength + 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, "Hi", latitude: 91)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, new string('a', MapPinRules.MaxMessageLength))).StatusCode);
    }

    [Fact]
    public async Task SessionPin_OnlyTheAuthorOrOwnerCanDeleteIt()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        await AuthTestHelpers.JoinSessionAsync(client, alice.AccessToken, session.InviteCode, role: "runner");
        await AuthTestHelpers.JoinSessionAsync(client, bob.AccessToken, session.InviteCode, role: "runner");
        var first = await ReadPinAsync(await CreateSessionPinAsync(client, session.SessionId, alice.AccessToken, "One"));
        var second = await ReadPinAsync(await CreateSessionPinAsync(client, session.SessionId, alice.AccessToken, "Two"));

        Assert.Equal(HttpStatusCode.Forbidden, await DeleteAsync(client, $"/sessions/{session.SessionId}/pins/{first.Id}", bob.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(client, $"/sessions/{session.SessionId}/pins/{first.Id}", alice.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(client, $"/sessions/{session.SessionId}/pins/{second.Id}", owner.AccessToken));
        Assert.Equal(HttpStatusCode.NotFound, await DeleteAsync(client, $"/sessions/{session.SessionId}/pins/{second.Id}", owner.AccessToken));

        Assert.Empty(await GetSessionPinsAsync(client, session.SessionId, bob.AccessToken));
    }

    [Fact]
    public async Task SessionPins_AreReadableByInviteCode_ForTheWebViewer()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, "Turn left");

        var pins = await client.GetFromJsonAsync<List<MapMessagePin>>($"/session-invites/{session.InviteCode}/pins");

        var pin = Assert.Single(pins!);
        Assert.Equal("Turn left", pin.Message);
        Assert.False(pin.IsMine);
        Assert.False(pin.CanDelete);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/session-invites/NOPE1234/pins")).StatusCode);
    }

    [Fact]
    public async Task SessionPins_StopAtTheCap()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "OW");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        for (var i = 0; i < MapPinRules.MaxPins; i++)
            Assert.Equal(HttpStatusCode.OK, (await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, $"Pin {i}")).StatusCode);

        var over = await CreateSessionPinAsync(client, session.SessionId, owner.AccessToken, "One too many");

        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
    }

    // ---- Casual runs ---------------------------------------------------------------------------

    [Fact]
    public async Task CasualPin_WhenNotOut_IsAConflict()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        Assert.Equal(HttpStatusCode.Conflict, (await CreateCasualPinAsync(client, alice.AccessToken, "Hi")).StatusCode);
    }

    [Fact]
    public async Task CasualPin_IsSeenByTheRunnerAndTheirCrew_OnlyTheRunnerCanDeleteIt()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");
        var stranger = await AuthTestHelpers.RegisterWithResponseAsync(client, "stranger", "ST");
        var code = await CrewApiTests.CreateCodeAsync(client, alice.AccessToken);
        using (var redeem = AuthTestHelpers.WithUserToken(HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken))
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(redeem)).StatusCode);
        await StartAndPostAsync(client, alice.AccessToken);

        var created = await CreateCasualPinAsync(client, alice.AccessToken, "Coffee stop");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var pin = await ReadPinAsync(created);

        var mine = Assert.Single((await GetLiveAsync(client, alice.AccessToken)).Pins!);
        Assert.True(mine.IsMine);
        Assert.True(mine.CanDelete);
        Assert.Equal("AL", mine.Author);

        var crewSees = Assert.Single((await GetLiveAsync(client, bob.AccessToken)).Pins!);
        Assert.Equal(pin.Id, crewSees.Id);
        Assert.Equal("AL", crewSees.Author);
        Assert.False(crewSees.IsMine);
        Assert.False(crewSees.CanDelete);

        Assert.Empty((await GetLiveAsync(client, stranger.AccessToken)).Pins!);
        Assert.Equal(HttpStatusCode.NotFound, await DeleteAsync(client, $"/casual/pins/{pin.Id}", bob.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, await DeleteAsync(client, $"/casual/pins/{pin.Id}", alice.AccessToken));
        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Pins!);
    }

    [Fact]
    public async Task CasualPins_ShowOnTheWatchLink_AndGoWhenTheRunEnds()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        using var linkRequest = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/watch-link", alice.AccessToken);
        var link = (await (await client.SendAsync(linkRequest)).Content.ReadFromJsonAsync<CasualWatchLinkResponse>())!;
        await CreateCasualPinAsync(client, alice.AccessToken, "Halfway");

        var watched = Assert.Single((await client.GetFromJsonAsync<CasualWatchView>(link.Path))!.Pins!);
        Assert.Equal("Halfway", watched.Message);
        Assert.False(watched.CanDelete);

        using var stop = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/stop", alice.AccessToken);
        await client.SendAsync(stop);
        Assert.Null((await client.GetFromJsonAsync<CasualWatchView>(link.Path))!.Pins);

        await StartAndPostAsync(client, alice.AccessToken);
        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Pins!);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> CreateSessionPinAsync(
        HttpClient client, string sessionId, string accessToken, string? message, double latitude = Lat)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Post, $"/sessions/{sessionId}/pins", accessToken);
        request.Content = JsonContent.Create(new { latitude, longitude = Lon, message });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CreateCasualPinAsync(HttpClient client, string accessToken, string message)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/pins", accessToken);
        request.Content = JsonContent.Create(new { latitude = Lat, longitude = Lon, message });
        return await client.SendAsync(request);
    }

    private static async Task<List<MapMessagePin>> GetSessionPinsAsync(HttpClient client, string sessionId, string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, $"/sessions/{sessionId}/pins", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<MapMessagePin>>())!;
    }

    private static async Task<MapMessagePin> ReadPinAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MapMessagePin>())!;
    }

    private static async Task<HttpStatusCode> DeleteAsync(HttpClient client, string url, string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Delete, url, accessToken);
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<CasualLiveResponse> GetLiveAsync(HttpClient client, string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(HttpMethod.Get, "/casual/live", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualLiveResponse>())!;
    }

    private static async Task StartAndPostAsync(HttpClient client, string accessToken)
    {
        using var start = AuthTestHelpers.WithUserToken(HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(start)).StatusCode);
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate("ignored", SessionStore.CasualSessionId, Lat, Lon, 90, DateTimeOffset.UtcNow),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }
}
