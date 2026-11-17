using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotWatcher.Server.Tests;

// "Find new crew": adding crew in person. Both people switch it on, see each other within 50 m, and
// become crew once both have accepted. See server/CrewFinder.cs and .ai/plans/crew-finding.md.
public class CrewFindingTests
{
    // Wellington waterfront. 0.00027 degrees of latitude is about 30 m; 0.0045 is about 500 m.
    private const double Lat = -41.2865;
    private const double Lon = 174.7762;
    private const double ThirtyMetres = 0.00027;
    private const double FiveHundredMetres = 0.0045;

    [Fact]
    public async Task TwoPeopleClose_SeeEachOther_ByNameOnly()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");

        await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat + ThirtyMetres, Lon);

        var seen = Assert.Single(bobState.People);
        Assert.Equal("AL", seen.DisplayName);
        Assert.NotEqual(alice.User.UserId, seen.Id);
        Assert.False(seen.YouAccepted);
        Assert.False(seen.AcceptedYou);

        var aliceState = await RefreshAsync(client, alice.AccessToken, Lat, Lon);
        Assert.Equal("BO", Assert.Single(aliceState.People).DisplayName);
    }

    [Fact]
    public async Task Response_HasNoPositionOrAccountId()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartAsync(client, alice.AccessToken, Lat, Lon);

        var response = await SendAsync(client, HttpMethod.Post, "/me/crew/finding", bob.AccessToken,
            new { latitude = Lat + ThirtyMetres, longitude = Lon, accuracy = 8.0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.String, root.GetProperty("expiresAt").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("added").ValueKind);
        var person = Assert.Single(root.GetProperty("people").EnumerateArray().ToList());
        Assert.Equal(JsonValueKind.String, person.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, person.GetProperty("displayName").ValueKind);
        Assert.Equal(JsonValueKind.False, person.GetProperty("youAccepted").ValueKind);
        Assert.Equal(JsonValueKind.False, person.GetProperty("acceptedYou").ValueKind);
        Assert.True(person.TryGetProperty("nickname", out _));

        Assert.DoesNotContain(alice.User.UserId, json);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("distance", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FarApart_AreNotListed()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");

        await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat + FiveHundredMetres, Lon);

        Assert.Empty(bobState.People);
    }

    [Fact]
    public async Task OnlyPeopleWithItOn_AreListed()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var carol = await RegisterAsync(client, "carol", "CA");
        await StartAsync(client, alice.AccessToken, Lat, Lon);
        await StartAsync(client, carol.AccessToken, Lat, Lon);

        var stop = await SendAsync(client, HttpMethod.Delete, "/me/crew/finding", carol.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
        var bobState = await StartAsync(client, bob.AccessToken, Lat, Lon);

        Assert.Equal("AL", Assert.Single(bobState.People).DisplayName);
    }

    [Fact]
    public async Task OneSidedAccept_DoesNotMakeCrew_ButShowsOnTheOtherSide()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat, Lon);

        var afterAccept = await AcceptAsync(client, bob.AccessToken, Assert.Single(bobState.People).Id);
        Assert.True(Assert.Single(afterAccept.People).YouAccepted);
        Assert.Empty(afterAccept.Added);

        var aliceState = await RefreshAsync(client, alice.AccessToken, Lat, Lon);
        Assert.True(Assert.Single(aliceState.People).AcceptedYou);
        Assert.Empty(await CrewIdsAsync(client, alice.AccessToken));
        Assert.Empty(await CrewIdsAsync(client, bob.AccessToken));
    }

    [Fact]
    public async Task BothAccept_MakesThemCrew_AndBothAreTold()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var aliceStart = await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat, Lon);
        aliceStart = await RefreshAsync(client, alice.AccessToken, Lat, Lon);

        await AcceptAsync(client, bob.AccessToken, Assert.Single(bobState.People).Id);
        var aliceDone = await AcceptAsync(client, alice.AccessToken, Assert.Single(aliceStart.People).Id);

        Assert.Equal(bob.User.UserId, Assert.Single(aliceDone.Added).UserId);
        Assert.Empty(aliceDone.People); // crew now, so nothing left to add
        var bobDone = await RefreshAsync(client, bob.AccessToken, Lat, Lon);
        Assert.Equal(alice.User.UserId, Assert.Single(bobDone.Added).UserId);

        Assert.Equal(new[] { bob.User.UserId }, await CrewIdsAsync(client, alice.AccessToken));
        Assert.Equal(new[] { alice.User.UserId }, await CrewIdsAsync(client, bob.AccessToken));
    }

    [Fact]
    public async Task AfterTwoMinutes_ItTurnsOff()
    {
        var clock = new AdjustableTimeProvider();
        using var factory = new DotWatcherApiFactory();
        using var timed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = timed.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartAsync(client, alice.AccessToken, Lat, Lon);
        await StartAsync(client, bob.AccessToken, Lat, Lon);

        // Both keep refreshing, as the apps do, so both fixes are fresh.
        clock.Advance(TimeSpan.FromSeconds(100));
        await RefreshAsync(client, alice.AccessToken, Lat, Lon);
        Assert.Single((await RefreshAsync(client, bob.AccessToken, Lat, Lon)).People);

        clock.Advance(TimeSpan.FromSeconds(25));
        var late = await SendAsync(client, HttpMethod.Put, "/me/crew/finding", alice.AccessToken,
            new { latitude = Lat, longitude = Lon, accuracy = 8.0 });
        Assert.Equal(HttpStatusCode.Gone, late.StatusCode);
    }

    [Fact]
    public async Task SwitchingOff_DropsUnansweredAccepts()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat, Lon);
        await AcceptAsync(client, bob.AccessToken, Assert.Single(bobState.People).Id);

        await SendAsync(client, HttpMethod.Delete, "/me/crew/finding", bob.AccessToken);
        var bobAgain = await StartAsync(client, bob.AccessToken, Lat, Lon);

        Assert.False(Assert.Single(bobAgain.People).YouAccepted);
        var aliceState = await RefreshAsync(client, alice.AccessToken, Lat, Lon);
        Assert.False(Assert.Single(aliceState.People).AcceptedYou);
    }

    [Fact]
    public async Task Blocked_AreNotListed_AndCannotBeAccepted()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat, Lon);
        var aliceToken = Assert.Single(bobState.People).Id;

        var block = await SendAsync(client, HttpMethod.Post, $"/me/blocks/{bob.User.UserId}", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);

        Assert.Empty((await RefreshAsync(client, bob.AccessToken, Lat, Lon)).People);
        var accept = await SendAsync(client, HttpMethod.Post, $"/me/crew/finding/{aliceToken}/accept", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task ExistingCrew_AreNotListed()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var code = await CrewApiTests.CreateCodeAsync(client, alice.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);

        await StartAsync(client, alice.AccessToken, Lat, Lon);
        var bobState = await StartAsync(client, bob.AccessToken, Lat, Lon);

        Assert.Empty(bobState.People);
    }

    [Fact]
    public async Task UnknownToken_IsNotFound_AndEndedWindowIsGone()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var bob = await RegisterAsync(client, "bob", "BO");

        var notOn = await SendAsync(client, HttpMethod.Post, "/me/crew/finding/abc123/accept", bob.AccessToken);
        Assert.Equal(HttpStatusCode.Gone, notOn.StatusCode);

        await StartAsync(client, bob.AccessToken, Lat, Lon);
        var unknown = await SendAsync(client, HttpMethod.Post, "/me/crew/finding/abc123/accept", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData(null, 174.0, 8.0)]
    [InlineData(-41.0, 174.0, 500.0)]
    [InlineData(-95.0, 174.0, 8.0)]
    public async Task BadPosition_IsRejected(double? latitude, double longitude, double accuracy)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var bob = await RegisterAsync(client, "bob", "BO");

        var response = await SendAsync(client, HttpMethod.Post, "/me/crew/finding", bob.AccessToken,
            new { latitude, longitude, accuracy });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignedOut_IsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/me/crew/finding", new { latitude = Lat, longitude = Lon });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<AuthResponse> RegisterAsync(HttpClient client, string username, string displayName) =>
        AuthTestHelpers.RegisterWithResponseAsync(client, username, displayName);

    private static async Task<CrewFindingState> StartAsync(HttpClient client, string token, double lat, double lon)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/me/crew/finding", token,
            new { latitude = lat, longitude = lon, accuracy = 8.0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrewFindingState>())!;
    }

    private static async Task<CrewFindingState> RefreshAsync(HttpClient client, string token, double lat, double lon)
    {
        var response = await SendAsync(client, HttpMethod.Put, "/me/crew/finding", token,
            new { latitude = lat, longitude = lon, accuracy = 8.0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrewFindingState>())!;
    }

    private static async Task<CrewFindingState> AcceptAsync(HttpClient client, string token, string id)
    {
        var response = await SendAsync(client, HttpMethod.Post, $"/me/crew/finding/{id}/accept", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrewFindingState>())!;
    }

    private static async Task<List<string>> CrewIdsAsync(HttpClient client, string token)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/crew", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrewOverview>())!.Crew.Select(m => m.UserId).ToList();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
