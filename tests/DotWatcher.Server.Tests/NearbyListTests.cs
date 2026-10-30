using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// GET /me/events/nearby with a position lists sessions people nearby can join.
// See .ai/plans/nearby-join.md (tasks N4, N7).
public class NearbyListTests
{
    // The organiser's meeting point. 0.0009 degrees of latitude is about 100 m.
    private const double Lat = -36.8500;
    private const double Lon = 174.7600;
    private const double About100m = 0.0009;

    [Fact]
    public async Task List_ShowsASessionInsideItsRadius_WithARoundedDistanceAndNoCoordinates()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser, radius: 250);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");

        var response = await ListAsync(client, joiner, Lat + About100m, Lon, accuracy: 5);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(session.SessionId, item.GetProperty("sessionId").GetString());
        Assert.Equal("Sunday long run", item.GetProperty("title").GetString());
        Assert.Contains("100 m", item.GetProperty("eyebrow").GetString());
        Assert.Contains("Alice", item.GetProperty("subtitle").GetString());
        Assert.DoesNotContain("-36.85", body);
        Assert.DoesNotContain("174.76", body);
    }

    [Fact]
    public async Task List_HidesASessionOutsideItsRadius()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await CreateNearbyAsync(client, organiser, radius: 100);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");

        var response = await ListAsync(client, joiner, Lat + 5 * About100m, Lon, accuracy: 5);

        Assert.Empty(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task List_ForgivesGpsAccuracy_ButOnlyUpToTheCap()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await CreateNearbyAsync(client, organiser, radius: 50, accuracy: 20);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var about115m = Lat + 1.15 * About100m;

        var withGoodAccuracy = await ListAsync(client, joiner, about115m, Lon, accuracy: 60);
        var withNoAccuracy = await ListAsync(client, joiner, about115m, Lon, accuracy: null);

        // 115 m away, radius 50: forgiven 50 (capped from 60) + 20 = 70, so 45 <= 50 passes.
        Assert.Single(await withGoodAccuracy.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
        // No joiner accuracy forgives only the organiser's 20: 95 > 50.
        Assert.Empty(await withNoAccuracy.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task List_ExcludesSwitchedOffSessions()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        await SendAsync(client, HttpMethod.Put, $"/sessions/{session.SessionId}/nearby", organiser, new { enabled = false });

        var response = await ListAsync(client, joiner, Lat, Lon, accuracy: 5);

        Assert.Empty(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task List_ExcludesEndedSessions()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateNearbyAsync(client, organiser);
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{session.SessionId}/membership", organiser);

        var response = await ListAsync(client, joiner, Lat, Lon, accuracy: 5);

        Assert.Empty(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task List_ExcludesSessionsTheCallerIsAlreadyIn()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await CreateNearbyAsync(client, organiser);

        var response = await ListAsync(client, organiser, Lat, Lon, accuracy: 5);

        Assert.Empty(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task List_ExcludesSessionsWhoseOrganiserIsBlockedEitherWay()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var aliceLogin = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");
        await CreateNearbyAsync(client, aliceLogin.AccessToken);
        var block = await SendAsync(client, HttpMethod.Post, $"/me/blocks/{aliceLogin.User.UserId}", bob.AccessToken);
        Assert.True(block.IsSuccessStatusCode);

        var response = await ListAsync(client, bob.AccessToken, Lat, Lon, accuracy: 5);

        Assert.Empty(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task List_WithoutAPosition_StillReturnsThePlaceholder()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await SendAsync(client, HttpMethod.Get, "/me/events/nearby", token);

        var item = Assert.Single(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
        Assert.Equal("placeholder-rogaine", item.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("lat=-36.85")]
    [InlineData("lat=95&lon=174.76")]
    [InlineData("lat=-36.85&lon=174.76&accuracy=-1")]
    public async Task List_RejectsAnInvalidPosition(string query)
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await SendAsync(client, HttpMethod.Get, $"/me/events/nearby?{query}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutTheEventsFeature_IsNotFound()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await ListAsync(client, token, Lat, Lon, accuracy: 5);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void IsWithinRadius_AppliesTheAccuracyAllowance()
    {
        // Radius 50 with both fixes at 20 m forgives 40 m: reach is 90 m.
        Assert.True(NearbyJoin.IsWithinRadius(90, 50, 20, 20));
        Assert.False(NearbyJoin.IsWithinRadius(91, 50, 20, 20));
        // Each accuracy is capped at 50 m, so even absurd values reach only 150 m.
        Assert.True(NearbyJoin.IsWithinRadius(150, 50, 500, 500));
        Assert.False(NearbyJoin.IsWithinRadius(151, 50, 500, 500));
        // A missing accuracy forgives nothing.
        Assert.True(NearbyJoin.IsWithinRadius(50, 50, null, null));
        Assert.False(NearbyJoin.IsWithinRadius(51, 50, null, null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(24, 0)]
    [InlineData(25, 50)]
    [InlineData(74, 50)]
    [InlineData(76, 100)]
    public void RoundDistance_RoundsToTheNearest50(double meters, int expected) =>
        Assert.Equal(expected, NearbyJoin.RoundDistance(meters));

    private static async Task<SessionMembership> CreateNearbyAsync(
        HttpClient client, string token, int? radius = null, double accuracy = 8)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/sessions", token, new
        {
            sessionName = "Sunday long run",
            nearby = true,
            nearbyRadiusMeters = radius,
            latitude = Lat,
            longitude = Lon,
            accuracy,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static Task<HttpResponseMessage> ListAsync(
        HttpClient client, string token, double lat, double lon, double? accuracy)
    {
        var query = FormattableString.Invariant($"lat={lat}&lon={lon}") + (accuracy is double a ? FormattableString.Invariant($"&accuracy={a}") : "");
        return SendAsync(client, HttpMethod.Get, $"/me/events/nearby?{query}", token);
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
