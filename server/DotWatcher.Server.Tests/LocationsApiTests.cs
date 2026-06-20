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
    public async Task PostLocation_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/location", TestLocation("Alice", "SUNSET23"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithInvalidBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await PostLocationAsync(
            client,
            TestLocation("Alice", "SUNSET23"),
            bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLocation_WithBearerToken_RecordsPositionAndReturnsParticipants()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await PostLocationAsync(client, TestLocation("Alice", "SUNSET23"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var participants = body.RootElement
            .GetProperty("participants")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Contains("Alice", participants);
    }

    [Fact]
    public async Task GetLocations_ForUnknownSession_ReturnsEmptyArray()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/locations/UNKNOWN");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var locations = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(locations);
        Assert.Empty(locations);
    }

    [Fact]
    public async Task GetLocations_ReturnsLatestLivePositionPerRunner()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await PostLocationAsync(client, TestLocation("Alice", "sunset23", latitude: -33.8680, timestampSeconds: 1));
        await PostLocationAsync(client, TestLocation("Alice", "sunset23", latitude: -33.8688, timestampSeconds: 2));
        await PostLocationAsync(client, TestLocation("Bob", "sunset23", latitude: -33.8695, timestampSeconds: 3));

        var response = await client.GetAsync("/locations/SUNSET23");

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
        string bearerToken = "test-token")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/location")
        {
            Content = JsonContent.Create(update),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

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
