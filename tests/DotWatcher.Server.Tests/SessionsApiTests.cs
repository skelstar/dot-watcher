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
    public async Task CreateSession_WithUserToken_ReturnsSessionAndInvite()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Post,
            "/sessions",
            token,
            new { sessionCode = "sunset23", displayName = "Trail Alice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<SessionMembership>();
        Assert.NotNull(session);
        Assert.Equal("SUNSET23", session.SessionCode);
        Assert.Equal("owner", session.Role);
        Assert.Equal("Trail Alice", session.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(session.InviteCode));
    }

    [Fact]
    public async Task JoinSession_WithInvite_AddsViewerOrRunnerMembership()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var viewerToken = await AuthTestHelpers.RegisterAsync(client, "viewer", "Viewer");

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            viewerToken,
            session.InviteCode,
            role: "viewer",
            displayName: "Roadside Viewer");

        Assert.Equal(session.SessionCode, joined.SessionCode);
        Assert.Equal("viewer", joined.Role);
        Assert.Equal("Roadside Viewer", joined.DisplayName);
    }

    [Fact]
    public async Task GetMySessions_WithUserToken_ReturnsMemberships()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await SendWithUserTokenAsync(client, HttpMethod.Get, "/me/sessions", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = await response.Content.ReadFromJsonAsync<List<SessionMembership>>();
        Assert.NotNull(sessions);
        Assert.Contains(sessions, session => session.SessionCode == "SUNSET23");
    }

    [Fact]
    public async Task GetSessions_WithoutAdminBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSessions_WithInvalidAdminBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Get,
            "/sessions",
            bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSessions_WithAdminBearerToken_ReturnsRecordedSessionsNewestFirst()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/older/recording",
            NdjsonLine("Alice", "older", timestampSeconds: 1));
        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/newer/recording",
            NdjsonLine("Bob", "newer", timestampSeconds: 2));

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessions = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Equal(new[] { "NEWER", "OLDER" }, sessions);
    }

    [Fact]
    public async Task DownloadRecording_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sessions/MISSING/recording");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DownloadRecording_ForMember_ReturnsNdjsonForRecordedSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Ignored", session.SessionCode, latitude: -33.8688),
            token);

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            $"/sessions/{session.SessionCode}/recording",
            token);

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
    public async Task DownloadRecording_WithoutMembership_ReturnsForbidden()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var outsiderToken = await AuthTestHelpers.RegisterAsync(client, "outsider", "Outsider");

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            "/sessions/SUNSET23/recording",
            outsiderToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithoutAdminBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/sessions/SUNSET23/recording",
            new StringContent(NdjsonLine("Alice", "SUNSET23"), Encoding.UTF8, "application/x-ndjson"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithInvalidAdminBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/SUNSET23/recording",
            NdjsonLine("Alice", "SUNSET23"),
            bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithMalformedNdjson_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/SUNSET23/recording",
            "{");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithAdminBearerToken_ReplacesRecordingAndListsSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/SUNSET23/recording",
            NdjsonLine("Bob", "SUNSET23"));

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/sunset23/recording",
            NdjsonLine("Alice", "ignored-session-code"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SUNSET23", body.RootElement.GetProperty("sessionCode").GetString());

        var sessionsResponse = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions");
        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Contains("SUNSET23", sessions);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/SUNSET23/recording");
        var line = Assert.Single((await recording.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var uploaded = JsonDocument.Parse(line);
        Assert.Equal("Alice", uploaded.RootElement.GetProperty("runnerName").GetString());
    }

    [Fact]
    public async Task DeleteRecording_RequiresAdminBearerTokenAndRemovesRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/SUNSET23/recording",
            NdjsonLine("Alice", "SUNSET23"));

        var unauthorized = await client.DeleteAsync("/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var deleted = await SendWithAdminBearerAsync(client, HttpMethod.Delete, "/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var download = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task DeleteRecording_ForMissingRecording_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Delete, "/sessions/MISSING/recording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClearSession_RequiresAdminBearerTokenAndClearsOnlyLivePositions()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await AuthTestHelpers.CreateSessionAsync(client, token);

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Ignored", "SUNSET23"),
            token);

        var unauthorized = await client.DeleteAsync("/sessions/SUNSET23");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var cleared = await SendWithAdminBearerAsync(client, HttpMethod.Delete, "/sessions/SUNSET23");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var livePositions = await LocationsApiTests.SendWithUserTokenAsync(client, HttpMethod.Get, "/locations/SUNSET23", token);
        var body = await livePositions.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(body);
        Assert.Empty(body);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/SUNSET23/recording");
        Assert.Equal(HttpStatusCode.OK, recording.StatusCode);
    }

    [Fact]
    public async Task MergeSession_WithAdminBearerToken_MovesSourceRecordsIntoTarget()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/SOURCE1/recording",
            NdjsonLine("Alice", "SOURCE1"));

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/target1/merge-from/source1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SOURCE1", body.RootElement.GetProperty("sourceCode").GetString());
        Assert.Equal("TARGET1", body.RootElement.GetProperty("targetCode").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("recordsMerged").GetInt32());

        var sourceRecording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/SOURCE1/recording");
        Assert.Equal(HttpStatusCode.NotFound, sourceRecording.StatusCode);

        var targetRecording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/TARGET1/recording");
        Assert.Equal(HttpStatusCode.OK, targetRecording.StatusCode);
    }

    [Fact]
    public async Task MergeSession_ForMissingSource_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/TARGET1/merge-from/MISSING");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendWithAdminBearerAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        string? body = null,
        string bearerToken = "test-token")
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson");

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendWithUserTokenAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        string accessToken,
        object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, uri, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    private static string NdjsonLine(string runnerName, string sessionCode, int timestampSeconds = 0) =>
        JsonSerializer.Serialize(LocationsApiTests.TestLocation(
            runnerName,
            sessionCode,
            timestampSeconds: timestampSeconds));
}
