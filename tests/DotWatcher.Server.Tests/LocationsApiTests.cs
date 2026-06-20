using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class LocationsApiTests
{
    [Fact]
    public async Task PostLocation_WithoutUserToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/location", TestLocation("Alice", "SUNSET23"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithInvalidUserToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await PostLocationAsync(
            client,
            TestLocation("Alice", "SUNSET23"),
            accessToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithoutRunnerMembership_ReturnsForbidden()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var viewerToken = await AuthTestHelpers.RegisterAsync(client, "viewer", "Viewer");
        await AuthTestHelpers.JoinSessionAsync(client, viewerToken, session.InviteCode, role: "viewer");

        var response = await PostLocationAsync(client, TestLocation("Viewer", session.SessionCode), viewerToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithMalformedJson_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/location")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithRunnerToken_RecordsPositionAndReturnsParticipants()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await PostLocationAsync(client, TestLocation("Ignored", session.SessionCode), token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var participants = body.RootElement
            .GetProperty("participants")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Contains("Alice", participants);
        Assert.DoesNotContain("Ignored", participants);
    }

    [Fact]
    public async Task GetLocations_WithoutUserToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/locations/SUNSET23");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_WithoutMembership_ReturnsForbidden()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var outsiderToken = await AuthTestHelpers.RegisterAsync(client, "outsider", "Outsider");

        var response = await SendWithUserTokenAsync(client, HttpMethod.Get, "/locations/SUNSET23", outsiderToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_ReturnsLatestLivePositionPerRunnerForMembers()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var aliceToken = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, aliceToken);
        var bobToken = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        await AuthTestHelpers.JoinSessionAsync(client, bobToken, session.InviteCode, role: "runner");

        await PostLocationAsync(client, TestLocation("Ignored", "sunset23", latitude: -33.8680, timestampSeconds: 1), aliceToken);
        await PostLocationAsync(client, TestLocation("Ignored", "sunset23", latitude: -33.8688, timestampSeconds: 2), aliceToken);
        await PostLocationAsync(client, TestLocation("Ignored", "sunset23", latitude: -33.8695, timestampSeconds: 3), bobToken);

        var response = await SendWithUserTokenAsync(client, HttpMethod.Get, "/locations/SUNSET23", aliceToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var locations = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(locations);

        var positionsByRunner = locations
            .SelectMany(runnerPositions => runnerPositions)
            .ToDictionary(position => position.RunnerName);

        Assert.Equal(2, positionsByRunner.Count);
        Assert.Equal(-33.8688, positionsByRunner["Alice"].Latitude);
        Assert.Equal(-33.8695, positionsByRunner["Bob"].Latitude);
    }

    internal static async Task<HttpResponseMessage> PostLocationAsync(
        HttpClient client,
        LocationUpdate update,
        string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/location")
        {
            Content = JsonContent.Create(update),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> SendWithUserTokenAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        string accessToken)
    {
        using var request = AuthTestHelpers.WithUserToken(method, uri, accessToken);
        return await client.SendAsync(request);
    }

    internal static LocationUpdate TestLocation(
        string runnerName,
        string sessionCode,
        double latitude = -41.17,
        int timestampSeconds = 0) =>
        new(
            RunnerName: runnerName,
            SessionCode: sessionCode,
            Latitude: latitude,
            Longitude: 174.7762,
            Heading: 270.5,
            Timestamp: new DateTimeOffset(2024, 11, 15, 9, 23, timestampSeconds, TimeSpan.Zero));
}
