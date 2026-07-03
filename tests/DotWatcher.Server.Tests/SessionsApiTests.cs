using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class SessionsApiTests
{
    private const string SomeSessionId    = "11111111-1111-1111-1111-111111111111";
    private const string OlderSessionId   = "22222222-2222-2222-2222-222222222222";
    private const string NewerSessionId   = "33333333-3333-3333-3333-333333333333";
    private const string TargetSessionId  = "44444444-4444-4444-4444-444444444444";
    private const string SourceSessionId  = "55555555-5555-5555-5555-555555555555";
    private const string IgnoredSessionId = "66666666-6666-6666-6666-666666666666";

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
            new { sessionName = "sunset23", displayName = "Trail Alice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<SessionMembership>();
        Assert.NotNull(session);
        Assert.Equal("SUNSET23", session.SessionName);
        Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
        Assert.Equal("runner", session.Role);
        Assert.Equal("Trail Alice", session.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(session.InviteCode));
    }

    [Fact]
    public async Task JoinSession_WithInvite_AddsViewerMembership()
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

        Assert.Equal(session.SessionId, joined.SessionId);
        Assert.Equal("viewer", joined.Role);
        Assert.Equal("Roadside Viewer", joined.DisplayName);
    }

    [Fact]
    public async Task JoinSession_WithRunnerRole_AddsRunnerMembership()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var creatorToken = await AuthTestHelpers.RegisterAsync(client, "creator", "Creator");
        var session = await AuthTestHelpers.CreateSessionAsync(client, creatorToken);
        var inviteeToken = await AuthTestHelpers.RegisterAsync(client, "invitee", "Invitee");

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            inviteeToken,
            session.InviteCode,
            role: "runner",
            displayName: "The Runner");

        Assert.Equal(session.SessionId, joined.SessionId);
        Assert.Equal("runner", joined.Role);
        Assert.Equal("The Runner", joined.DisplayName);

        var writeAttempt = await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            inviteeToken);

        Assert.Equal(HttpStatusCode.OK, writeAttempt.StatusCode);
    }

    [Fact]
    public async Task JoinSession_WithOwnerRole_StillAddsViewerMembership()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var creatorToken = await AuthTestHelpers.RegisterAsync(client, "creator", "Creator");
        var session = await AuthTestHelpers.CreateSessionAsync(client, creatorToken);
        var inviteeToken = await AuthTestHelpers.RegisterAsync(client, "invitee", "Invitee");

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            inviteeToken,
            session.InviteCode,
            role: "owner",
            displayName: "Not The Owner");

        Assert.Equal(session.SessionId, joined.SessionId);
        Assert.Equal("viewer", joined.Role);
        Assert.Equal("Not The Owner", joined.DisplayName);

        var writeAttempt = await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            inviteeToken);

        Assert.Equal(HttpStatusCode.Forbidden, writeAttempt.StatusCode);
    }

    [Fact]
    public async Task JoinSession_WithExistingRunnerMembership_PreservesRunnerRole()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var creatorToken = await AuthTestHelpers.RegisterAsync(client, "creator", "Creator");
        var session = await AuthTestHelpers.CreateSessionAsync(client, creatorToken);

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            creatorToken,
            session.InviteCode,
            displayName: "Still The Creator");

        Assert.Equal(session.SessionId, joined.SessionId);
        Assert.Equal("runner", joined.Role);
        Assert.Equal("Still The Creator", joined.DisplayName);

        var writeAttempt = await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            creatorToken);

        Assert.Equal(HttpStatusCode.OK, writeAttempt.StatusCode);
    }

    [Fact]
    public async Task GetLocationsByInviteCode_WithValidInvite_ReturnsPositionsWithoutAuth()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);

        await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Owner", session.SessionId),
            ownerToken);

        var response = await client.GetAsync($"/session-invites/{session.InviteCode}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var positions = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(positions);
        Assert.Contains(positions, group => group.Any(p => p.RunnerName == "Owner"));
    }

    [Fact]
    public async Task GetLocationsByInviteCode_WithUnknownInvite_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/session-invites/NOPE99/locations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRecordingByInviteCode_WithValidInvite_ReturnsNdjsonWithoutAuth()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId, latitude: -33.8688),
            token);

        var response = await client.GetAsync($"/session-invites/{session.InviteCode}/recording");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.Equal("Alice", json.RootElement.GetProperty("runnerName").GetString());
    }

    [Fact]
    public async Task GetRecordingByInviteCode_WithUnknownInvite_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/session-invites/NOPE99/recording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRecordingByInviteCode_WithNoRecordingYet_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await client.GetAsync($"/session-invites/{session.InviteCode}/recording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        Assert.Contains(sessions, session => session.SessionName == "SUNSET23");
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

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{OlderSessionId}/recording",
            NdjsonLine("Alice", OlderSessionId, timestampSeconds: 1));
        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{NewerSessionId}/recording",
            NdjsonLine("Bob", NewerSessionId, timestampSeconds: 2));

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessions = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Equal(new[] { NewerSessionId, OlderSessionId }, sessions);
    }

    [Fact]
    public async Task DownloadRecording_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sessions/missing-session-id/recording");

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
            LocationsApiTests.TestLocation("Ignored", session.SessionId, latitude: -33.8688),
            token);

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            $"/sessions/{session.SessionId}/recording",
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.Equal("Alice", json.RootElement.GetProperty("runnerName").GetString());
        Assert.Equal(session.SessionId, json.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(-33.8688, json.RootElement.GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task DownloadRecording_WithoutMembership_ReturnsForbidden()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var outsiderToken = await AuthTestHelpers.RegisterAsync(client, "outsider", "Outsider");

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            $"/sessions/{session.SessionId}/recording",
            outsiderToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadRecording_WithInvalidAdminSessionId_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions//recording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithoutAdminBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/sessions/{SomeSessionId}/recording",
            new StringContent(NdjsonLine("Alice", SomeSessionId), Encoding.UTF8, "application/x-ndjson"));

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
            $"/sessions/{SomeSessionId}/recording",
            NdjsonLine("Alice", SomeSessionId),
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
            $"/sessions/{SomeSessionId}/recording",
            "{");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithInvalidLocation_ReturnsBadRequestAndPreservesExistingRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{SomeSessionId}/recording",
            NdjsonLine("Bob", SomeSessionId));

        var invalid = JsonSerializer.Serialize(
            LocationsApiTests.TestLocation("Alice", SomeSessionId) with { Latitude = 91 });

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{SomeSessionId}/recording",
            invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{SomeSessionId}/recording");
        Assert.Equal(HttpStatusCode.OK, recording.StatusCode);
        var line = Assert.Single((await recording.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var uploaded = JsonDocument.Parse(line);
        Assert.Equal("Bob", uploaded.RootElement.GetProperty("runnerName").GetString());
    }

    [Fact]
    public async Task UploadRecording_WithMissingTimestamp_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var missingTimestamp = JsonSerializer.Serialize(new
        {
            runnerName = "Alice",
            sessionId = SomeSessionId,
            latitude = -41.17,
            longitude = 174.7762,
            heading = 270.5,
        });

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{SomeSessionId}/recording",
            missingTimestamp);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithEmptyBody_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{SomeSessionId}/recording",
            "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithAdminBearerToken_ReplacesRecordingAndListsSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{TargetSessionId}/recording",
            NdjsonLine("Bob", TargetSessionId));

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{TargetSessionId}/recording",
            NdjsonLine("Alice", IgnoredSessionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TargetSessionId, body.RootElement.GetProperty("sessionId").GetString());

        var sessionsResponse = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions");
        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Contains(TargetSessionId, sessions);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{TargetSessionId}/recording");
        var line = Assert.Single((await recording.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var uploaded = JsonDocument.Parse(line);
        Assert.Equal("Alice", uploaded.RootElement.GetProperty("runnerName").GetString());
    }

    [Fact]
    public async Task DeleteRecording_RequiresAdminBearerTokenAndRemovesRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{SomeSessionId}/recording",
            NdjsonLine("Alice", SomeSessionId));

        var unauthorized = await client.DeleteAsync($"/sessions/{SomeSessionId}/recording");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var deleted = await SendWithAdminBearerAsync(client, HttpMethod.Delete, $"/sessions/{SomeSessionId}/recording");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var download = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{SomeSessionId}/recording");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task DeleteRecording_ForMissingRecording_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Delete, "/sessions/missing-session-id/recording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClearSession_RequiresAdminBearerTokenAndClearsOnlyLivePositions()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            token);

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/recording",
            NdjsonLine("Ignored", session.SessionId));

        var unauthorized = await client.DeleteAsync($"/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var cleared = await SendWithAdminBearerAsync(client, HttpMethod.Delete, $"/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var livePositions = await LocationsApiTests.SendWithUserTokenAsync(client, HttpMethod.Get, $"/locations/{session.SessionId}", token);
        var body = await livePositions.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(body);
        Assert.Empty(body);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{session.SessionId}/recording");
        Assert.Equal(HttpStatusCode.OK, recording.StatusCode);
    }

    [Fact]
    public async Task MergeSession_WithAdminBearerToken_MovesSourceRecordsIntoTarget()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{SourceSessionId}/recording",
            NdjsonLine("Alice", SourceSessionId));

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{TargetSessionId}/merge-from/{SourceSessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SourceSessionId, body.RootElement.GetProperty("sourceId").GetString());
        Assert.Equal(TargetSessionId, body.RootElement.GetProperty("targetId").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("recordsMerged").GetInt32());

        var sourceRecording = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{SourceSessionId}/recording");
        Assert.Equal(HttpStatusCode.NotFound, sourceRecording.StatusCode);

        var targetRecording = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{TargetSessionId}/recording");
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
            $"/sessions/{TargetSessionId}/merge-from/missing-session-id");

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

    private static string NdjsonLine(string runnerName, string sessionId, int timestampSeconds = 0) =>
        JsonSerializer.Serialize(LocationsApiTests.TestLocation(
            runnerName,
            sessionId,
            timestampSeconds: timestampSeconds));
}
