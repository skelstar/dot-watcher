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
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await client.PostAsJsonAsync("/location", TestLocation("Alice", session.SessionId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithInvalidUserToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await PostLocationAsync(
            client,
            TestLocation("Alice", session.SessionId),
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

        var response = await PostLocationAsync(client, TestLocation("Viewer", session.SessionId), viewerToken);

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

        var response = await PostLocationAsync(client, TestLocation("Ignored", session.SessionId), token);

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

    [Theory]
    [InlineData(91, 174.7762, 270.5)]
    [InlineData(-41.17, 181, 270.5)]
    [InlineData(-41.17, 174.7762, 361)]
    public async Task PostLocation_WithInvalidGpsPayload_ReturnsBadRequest(
        double latitude,
        double longitude,
        double heading)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await PostLocationAsync(
            client,
            TestLocation("Ignored", session.SessionId) with
            {
                Latitude = latitude,
                Longitude = longitude,
                Heading = heading,
            },
            token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithDefaultTimestamp_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await PostLocationAsync(
            client,
            TestLocation("Ignored", session.SessionId) with { Timestamp = default },
            token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithMissingLatitude_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var body = JsonSerializer.Serialize(new
        {
            runnerName = "Ignored",
            sessionId = session.SessionId,
            longitude = 174.7762,
            heading = 270.5,
            timestamp = "2024-11-15T09:23:00Z",
        });

        var response = await PostRawLocationAsync(client, body, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_WithoutUserToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await client.GetAsync($"/locations/{session.SessionId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_WithoutMembership_ReturnsForbidden()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var outsiderToken = await AuthTestHelpers.RegisterAsync(client, "outsider", "Outsider");

        var response = await SendWithUserTokenAsync(client, HttpMethod.Get, $"/locations/{session.SessionId}", outsiderToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_ReturnsLatestLivePositionForMember()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var aliceToken = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, aliceToken);

        await PostLocationAsync(client, TestLocation("Ignored", session.SessionId, latitude: -33.8680, timestampSeconds: 1), aliceToken);
        await PostLocationAsync(client, TestLocation("Ignored", session.SessionId, latitude: -33.8688, timestampSeconds: 2), aliceToken);

        var response = await SendWithUserTokenAsync(client, HttpMethod.Get, $"/locations/{session.SessionId}", aliceToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var locations = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(locations);

        var positionsByRunner = locations
            .SelectMany(runnerPositions => runnerPositions)
            .ToDictionary(position => position.RunnerName);

        Assert.Single(positionsByRunner);
        Assert.Equal(-33.8688, positionsByRunner["Alice"].Latitude);
    }

    [Fact]
    public async Task GetLocations_AfterMemberDisplayNameChangeAndAccountDeletion_DoesNotReturnStaleLivePosition()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "Owner");
        var runner = await AuthTestHelpers.RegisterWithResponseAsync(client, "runner", "Runner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);

        await AuthTestHelpers.JoinSessionAsync(
            client,
            runner.AccessToken,
            session.InviteCode,
            displayName: "First Name");
        using (var promote = AuthTestHelpers.WithUserToken(
            HttpMethod.Post,
            $"/sessions/{session.SessionId}/members/{runner.User.UserId}/role",
            owner.AccessToken))
        {
            promote.Content = JsonContent.Create(new { role = "runner" });
            var promoteResponse = await client.SendAsync(promote);
            Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);
        }

        await PostLocationAsync(
            client,
            TestLocation("Ignored", session.SessionId, latitude: -33.8680, timestampSeconds: 1),
            runner.AccessToken);

        await AuthTestHelpers.JoinSessionAsync(
            client,
            runner.AccessToken,
            session.InviteCode,
            displayName: "Second Name");

        await PostLocationAsync(
            client,
            TestLocation("Ignored", session.SessionId, latitude: -33.8688, timestampSeconds: 2),
            runner.AccessToken);

        var renamedResponse = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            $"/locations/{session.SessionId}",
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, renamedResponse.StatusCode);
        var renamedLocations = await renamedResponse.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(renamedLocations);
        var renamedPositions = renamedLocations.SelectMany(runnerPositions => runnerPositions).ToList();
        var renamedPosition = Assert.Single(renamedPositions);
        Assert.Equal("Second Name", renamedPosition.RunnerName);
        Assert.Equal(-33.8688, renamedPosition.Latitude);

        using (var deleteRunner = AuthTestHelpers.WithUserToken(HttpMethod.Delete, "/me", runner.AccessToken))
        {
            var deleteResponse = await client.SendAsync(deleteRunner);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        }

        var afterDeleteResponse = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            $"/locations/{session.SessionId}",
            owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, afterDeleteResponse.StatusCode);
        var afterDeleteLocations = await afterDeleteResponse.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(afterDeleteLocations);
        Assert.Empty(afterDeleteLocations.SelectMany(runnerPositions => runnerPositions));
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

    private static async Task<HttpResponseMessage> PostRawLocationAsync(
        HttpClient client,
        string json,
        string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/location")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
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
        string sessionId,
        double latitude = -41.17,
        int timestampSeconds = 0) =>
        new(
            RunnerName: runnerName,
            SessionId: sessionId,
            Latitude: latitude,
            Longitude: 174.7762,
            Heading: 270.5,
            Timestamp: new DateTimeOffset(2024, 11, 15, 9, 23, timestampSeconds, TimeSpan.Zero));
}
