using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// The organiser's Ping: PUT /sessions/{id}/nearby. See .ai/plans/nearby-join.md (task N3).
public class NearbyPingTests
{
    private static readonly object Fix = new { latitude = -36.86, longitude = 174.77, accuracy = 8 };

    [Fact]
    public async Task Ping_ByTheOwner_ReturnsTheCurrentSetting()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbySessionAsync(client, token);

        var response = await PutAsync(client, token, session.SessionId, Fix);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<NearbyStatus>();
        Assert.Equal(new NearbyStatus(true, 100), status);
    }

    [Fact]
    public async Task Ping_ByAnotherUser_IsForbidden()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var bob = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var session = await CreateNearbySessionAsync(client, alice);

        var response = await PutAsync(client, bob, session.SessionId, Fix);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ping_ForAnUnknownSession_IsForbiddenToo()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await PutAsync(client, token, "no-such-session", Fix);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithoutTheEventsFeature_IsNotFound()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await PutAsync(client, token, "any", Fix);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithoutSignIn_IsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/sessions/any/nearby", Fix);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(95.0, 174.77, 8.0)]
    [InlineData(-36.86, 174.77, 250.0)]
    public async Task Ping_RejectsMissingOrBadFix(double? latitude, double? longitude, double? accuracy)
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbySessionAsync(client, token);

        var response = await PutAsync(client, token, session.SessionId, new { latitude, longitude, accuracy });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ping_RejectsARadiusOutsideTheFourChoices()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbySessionAsync(client, token);

        var response = await PutAsync(client, token, session.SessionId,
            new { latitude = -36.86, longitude = 174.77, radiusMeters = 20 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ping_CanChangeTheRadius()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbySessionAsync(client, token);

        var response = await PutAsync(client, token, session.SessionId,
            new { latitude = -36.86, longitude = 174.77, accuracy = 8, radiusMeters = 250 });

        Assert.Equal(new NearbyStatus(true, 250), await response.Content.ReadFromJsonAsync<NearbyStatus>());
    }

    [Fact]
    public async Task SwitchingOff_NeedsNoFix_AndGivingARadiusTurnsItBackOn()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbySessionAsync(client, token);

        var off = await PutAsync(client, token, session.SessionId, new { enabled = false });
        var on = await PutAsync(client, token, session.SessionId,
            new { latitude = -36.86, longitude = 174.77, accuracy = 8, radiusMeters = 50 });

        Assert.Equal(new NearbyStatus(false, 100), await off.Content.ReadFromJsonAsync<NearbyStatus>());
        Assert.Equal(new NearbyStatus(true, 50), await on.Content.ReadFromJsonAsync<NearbyStatus>());
    }

    [Fact]
    public async Task Ping_OnAnEndedSession_IsGone()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbySessionAsync(client, token);
        using var leave = AuthTestHelpers.WithUserToken(HttpMethod.Delete, $"/me/sessions/{session.SessionId}/membership", token);
        Assert.True((await client.SendAsync(leave)).IsSuccessStatusCode);

        var response = await PutAsync(client, token, session.SessionId, Fix);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    private static async Task<SessionMembership> CreateNearbySessionAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = JsonContent.Create(new
            {
                sessionName = "Sunday long run",
                nearby = true,
                latitude = -36.85,
                longitude = 174.76,
                accuracy = 12,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string token, string sessionId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/sessions/{sessionId}/nearby")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
