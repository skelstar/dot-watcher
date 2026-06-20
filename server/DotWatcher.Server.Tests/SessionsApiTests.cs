using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class SessionsApiTests
{
    [Fact]
    public async Task GetSessions_ReturnsRecordedSessionsNewestFirst()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "older", timestampSeconds: 1));
        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Bob", "newer", timestampSeconds: 2));

        var response = await client.GetAsync("/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessions = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Equal(new[] { "NEWER", "OLDER" }, sessions);
    }

    [Fact]
    public async Task DownloadRecording_ForUnknownSession_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sessions/MISSING/recording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadRecording_ReturnsNdjsonForRecordedSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "SUNSET23", latitude: -33.8688));

        var response = await client.GetAsync("/sessions/SUNSET23/recording");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.Equal("Alice", json.RootElement.GetProperty("runnerName").GetString());
        Assert.Equal("SUNSET23", json.RootElement.GetProperty("sessionCode").GetString());
        Assert.Equal(-33.8688, json.RootElement.GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task UploadRecording_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/sessions/SUNSET23/recording",
            new StringContent(NdjsonLine("Alice", "SUNSET23"), Encoding.UTF8, "application/x-ndjson"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithBearerToken_ReplacesRecordingAndListsSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Bob", "SUNSET23"));

        var response = await SendWithBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/sunset23/recording",
            NdjsonLine("Alice", "ignored-session-code"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUNSET23", body.RootElement.GetProperty("sessionCode").GetString());

        var sessions = await client.GetFromJsonAsync<List<string>>("/sessions");
        Assert.NotNull(sessions);
        Assert.Contains("SUNSET23", sessions);

        var recording = await client.GetStringAsync("/sessions/SUNSET23/recording");
        var line = Assert.Single(recording.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var uploaded = JsonDocument.Parse(line);
        Assert.Equal("Alice", uploaded.RootElement.GetProperty("runnerName").GetString());
    }

    [Fact]
    public async Task DeleteRecording_RequiresBearerTokenAndRemovesRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "SUNSET23"));

        var unauthorized = await client.DeleteAsync("/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var deleted = await SendWithBearerAsync(client, HttpMethod.Delete, "/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var download = await client.GetAsync("/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task ClearSession_RequiresBearerTokenAndClearsOnlyLivePositions()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "SUNSET23"));

        var unauthorized = await client.DeleteAsync("/sessions/SUNSET23");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var cleared = await SendWithBearerAsync(client, HttpMethod.Delete, "/sessions/SUNSET23");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var livePositions = await client.GetFromJsonAsync<List<List<RunnerPosition>>>("/locations/SUNSET23");
        Assert.NotNull(livePositions);
        Assert.Empty(livePositions);

        var recording = await client.GetAsync("/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.OK, recording.StatusCode);
    }

    [Fact]
    public async Task MergeSession_WithBearerToken_MovesSourceRecordsIntoTarget()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "SOURCE1"));

        var response = await SendWithBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/target1/merge-from/source1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SOURCE1", body.RootElement.GetProperty("sourceCode").GetString());
        Assert.Equal("TARGET1", body.RootElement.GetProperty("targetCode").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("recordsMerged").GetInt32());

        var sourceRecording = await client.GetAsync("/sessions/SOURCE1/recording");
        Assert.Equal(HttpStatusCode.NotFound, sourceRecording.StatusCode);

        var targetRecording = await client.GetAsync("/sessions/TARGET1/recording");
        Assert.Equal(HttpStatusCode.OK, targetRecording.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendWithBearerAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        string? body = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson");

        return await client.SendAsync(request);
    }

    private static string NdjsonLine(string runnerName, string sessionCode) =>
        JsonSerializer.Serialize(LocationsApiTests.TestLocation(runnerName, sessionCode));
}
