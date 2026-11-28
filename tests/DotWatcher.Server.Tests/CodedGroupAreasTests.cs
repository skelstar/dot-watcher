using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// A group that asks for an entry code shows its join area (location and radius) on the casual map of
// "groups" accounts out within 5 km: without the code nobody can join, so the meeting point isn't secret.
// Groups without a code keep it hidden. See .ai/plans/go-and-groups.md.
public class CodedGroupAreasTests
{
    private const double JoinLatitude = -41.2958;
    private const double JoinLongitude = 174.7693;
    // About 1.1 km north of the join location: outside any join radius, inside the 5 km map radius.
    private const double NearLatitude = -41.2858;
    // About 11 km north: outside the map radius.
    private const double FarLatitude = -41.1958;

    private static DotWatcherApiFactory Factory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual, Features.Events, Features.Groups, Features.CreateGroups] };

    [Fact]
    public async Task CodedGroup_ShowsItsAreaToSomeoneOutNearby()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "SA");
        var group = await CreateNearbyAsync(client, kiri.AccessToken, entryCode: "K7Q4", radius: 250);
        await GoAsync(client, sam.AccessToken, NearLatitude);

        var live = await GetLiveAsync(client, sam.AccessToken);

        var area = Assert.Single(live.GroupAreas!);
        Assert.Equal(group.SessionId, area.SessionId);
        Assert.Equal("Hunua 50k", area.Name);
        Assert.Equal(JoinLatitude, area.Latitude, 4);
        Assert.Equal(JoinLongitude, area.Longitude, 4);
        Assert.Equal(250, area.RadiusMeters);
    }

    [Fact]
    public async Task SomeoneWhoLeftTheGroup_StillSeesItsArea()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "SA");
        var group = await CreateNearbyAsync(client, kiri.AccessToken, entryCode: "K7Q4");
        await AuthTestHelpers.JoinSessionAsync(client, sam.AccessToken, group.InviteCode, role: "runner", displayName: "Sam");
        var leave = await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{group.SessionId}/membership", sam.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        await GoAsync(client, sam.AccessToken, NearLatitude);

        var live = await GetLiveAsync(client, sam.AccessToken);

        Assert.Equal(group.SessionId, Assert.Single(live.GroupAreas!).SessionId);
    }

    [Fact]
    public async Task GroupWithoutACode_KeepsItsAreaHidden()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "SA");
        await CreateNearbyAsync(client, kiri.AccessToken, entryCode: null);
        await GoAsync(client, sam.AccessToken, NearLatitude);

        var live = await GetLiveAsync(client, sam.AccessToken);

        Assert.Empty(live.GroupAreas ?? []);
    }

    [Fact]
    public async Task FarAway_TheAreaIsNotSent()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "SA");
        await CreateNearbyAsync(client, kiri.AccessToken, entryCode: "K7Q4");
        await GoAsync(client, sam.AccessToken, FarLatitude);

        var live = await GetLiveAsync(client, sam.AccessToken);

        Assert.Empty(live.GroupAreas ?? []);
    }

    [Fact]
    public async Task EndedGroup_IsNotSent()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "SA");
        var group = await CreateNearbyAsync(client, kiri.AccessToken, entryCode: "K7Q4");
        Assert.True((await SendAsync(client, HttpMethod.Post, $"/sessions/{group.SessionId}/end", kiri.AccessToken)).IsSuccessStatusCode);
        await GoAsync(client, sam.AccessToken, NearLatitude);

        var live = await GetLiveAsync(client, sam.AccessToken);

        Assert.Empty(live.GroupAreas ?? []);
    }

    [Fact]
    public async Task TheOrganiser_IsNotSentTheirOwnArea()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        await CreateNearbyAsync(client, kiri.AccessToken, entryCode: "K7Q4");
        await GoAsync(client, kiri.AccessToken, NearLatitude);

        var live = await GetLiveAsync(client, kiri.AccessToken);

        Assert.Empty(live.GroupAreas ?? []);
    }

    [Fact]
    public async Task WithoutTheGroupsFeature_TheAreaIsNotSent()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Crew, Features.Casual, Features.Events] };
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterWithResponseAsync(client, "kiri", "KI");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "SA");
        await SetFeaturesAsync(client, kiri.User.UserId, [Features.Groups, Features.CreateGroups]);
        await CreateNearbyAsync(client, kiri.AccessToken, entryCode: "K7Q4");
        await GoAsync(client, sam.AccessToken, NearLatitude);

        var live = await GetLiveAsync(client, sam.AccessToken);

        Assert.Null(live.GroupAreas);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static async Task<SessionMembership> CreateNearbyAsync(HttpClient client, string token, string? entryCode, int radius = 100)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/sessions", token, new
        {
            sessionName = "Hunua 50k",
            nearby = true,
            nearbyRadiusMeters = radius,
            latitude = JoinLatitude,
            longitude = JoinLongitude,
            accuracy = 8,
            entryCode,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static async Task GoAsync(HttpClient client, string accessToken, double latitude)
    {
        var start = await SendAsync(client, HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate(
                RunnerName: "ignored",
                SessionId: SessionStore.CasualSessionId,
                Latitude: latitude,
                Longitude: JoinLongitude,
                Heading: 0,
                Timestamp: DateTimeOffset.UtcNow),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    private static async Task<CasualLiveResponse> GetLiveAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/casual/live", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualLiveResponse>())!;
    }

    private static async Task SetFeaturesAsync(HttpClient client, string userId, string[] features)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/admin/users/{userId}/features")
        {
            Content = JsonContent.Create(new { features }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        Assert.True((await client.SendAsync(request)).IsSuccessStatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
