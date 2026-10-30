using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// POST /nearby/sessions/{id}/join. See .ai/plans/nearby-join.md (tasks N5, N7).
public class NearbyJoinApiTests
{
    private const double Lat = -36.8500;
    private const double Lon = 174.7600;
    private const double About100m = 0.0009;

    [Fact]
    public async Task Join_InsideTheRadius_MakesTheCallerARunner_AndReturnsTheInviteJoinShape()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");

        var response = await JoinAsync(client, joiner, session.SessionId, Lat + 0.5 * About100m, Lon, accuracy: 5);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(session.SessionId, json.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("runner", json.RootElement.GetProperty("role").GetString());
        Assert.Equal(session.InviteCode, json.RootElement.GetProperty("inviteCode").GetString());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("participants").ValueKind);

        var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", joiner);
        var sessions = await mine.Content.ReadFromJsonAsync<List<SessionMembership>>();
        Assert.Contains(sessions!, s => s.SessionId == session.SessionId && s.Role == "runner");
    }

    [Fact]
    public async Task Join_OutsideTheRadius_IsNotAvailable_AndAddsNobody()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");

        var response = await JoinAsync(client, joiner, session.SessionId, Lat + 10 * About100m, Lon, accuracy: 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", joiner);
        Assert.Empty(await mine.Content.ReadFromJsonAsync<List<SessionMembership>>() ?? []);
    }

    [Fact]
    public async Task Join_EveryRefusalLooksTheSame()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var aliceLogin = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");
        var carol = await AuthTestHelpers.RegisterAsync(client, "carol", "Carol");

        var nearby = await CreateNearbyAsync(client, aliceLogin.AccessToken);
        var switchedOff = await CreateNearbyAsync(client, aliceLogin.AccessToken);
        await SendAsync(client, HttpMethod.Put, $"/sessions/{switchedOff.SessionId}/nearby", aliceLogin.AccessToken,
            new { enabled = false });
        var notNearby = await SendAsync(client, HttpMethod.Post, "/sessions", aliceLogin.AccessToken,
            new { sessionName = "plain01" });
        var plain = (await notNearby.Content.ReadFromJsonAsync<SessionMembership>())!;

        // Bob blocks Alice, so Bob is refused her otherwise-valid session.
        await SendAsync(client, HttpMethod.Post, $"/me/blocks/{aliceLogin.User.UserId}", bob.AccessToken);

        var tooFar = await JoinAsync(client, carol, nearby.SessionId, Lat + 10 * About100m, Lon, accuracy: 5);
        var off = await JoinAsync(client, carol, switchedOff.SessionId, Lat, Lon, accuracy: 5);
        var notNearbySession = await JoinAsync(client, carol, plain.SessionId, Lat, Lon, accuracy: 5);
        var unknown = await JoinAsync(client, carol, "no-such-session", Lat, Lon, accuracy: 5);
        var blocked = await JoinAsync(client, bob.AccessToken, nearby.SessionId, Lat, Lon, accuracy: 5);

        var bodies = new List<string>();
        foreach (var response in new[] { tooFar, off, notNearbySession, unknown, blocked })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }
        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task Join_AnEndedSession_IsNotAvailable()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{session.SessionId}/membership", organiser);

        var response = await JoinAsync(client, joiner, session.SessionId, Lat, Lon, accuracy: 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Join_UsesTheDisplayNameGiven_ElseTheAccountName()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");

        var response = await SendAsync(client, HttpMethod.Post, $"/nearby/sessions/{session.SessionId}/join", joiner,
            new { latitude = Lat, longitude = Lon, accuracy = 5, displayName = "Trail Bob" });

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Trail Bob", json.RootElement.GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(95.0, 174.76)]
    public async Task Join_RejectsAMissingOrInvalidPosition(double? lat, double? lon)
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await SendAsync(client, HttpMethod.Post, "/nearby/sessions/any/join", token,
            new { latitude = lat, longitude = lon });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Join_WithoutTheEventsFeature_IsNotFound()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await JoinAsync(client, token, "any", Lat, Lon, accuracy: 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Join_WithoutSignIn_IsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/nearby/sessions/any/join", new { latitude = Lat, longitude = Lon });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<SessionMembership> CreateNearbyAsync(HttpClient client, string token)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/sessions", token, new
        {
            sessionName = "Sunday long run",
            nearby = true,
            latitude = Lat,
            longitude = Lon,
            accuracy = 8,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static Task<HttpResponseMessage> JoinAsync(
        HttpClient client, string token, string sessionId, double lat, double lon, double? accuracy) =>
        SendAsync(client, HttpMethod.Post, $"/nearby/sessions/{sessionId}/join", token,
            new { latitude = lat, longitude = lon, accuracy });

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
