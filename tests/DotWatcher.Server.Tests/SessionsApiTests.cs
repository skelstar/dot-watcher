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
            new { sessionName = "sunset23", displayName = "Trail Alice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<SessionMembership>();
        Assert.NotNull(session);
        Assert.Equal("SUNSET23", session.SessionName);
        Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
        Assert.Equal("owner", session.Role);
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

    [Theory]
    [InlineData("runner")]
    [InlineData("owner")]
    public async Task JoinSession_WithRequestedPrivilegedRole_StillAddsViewerMembership(string requestedRole)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var inviteeToken = await AuthTestHelpers.RegisterAsync(client, "invitee", "Invitee");

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            inviteeToken,
            session.InviteCode,
            role: requestedRole,
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
    public async Task JoinSession_WithExistingOwnerMembership_PreservesOwnerRole()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            ownerToken,
            session.InviteCode,
            displayName: "Still The Owner");

        Assert.Equal(session.SessionId, joined.SessionId);
        Assert.Equal("owner", joined.Role);
        Assert.Equal("Still The Owner", joined.DisplayName);

        var writeAttempt = await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            ownerToken);

        Assert.Equal(HttpStatusCode.OK, writeAttempt.StatusCode);
    }

    [Fact]
    public async Task UpdateSessionMemberRole_WithOwnerToken_PromotesViewerToRunner()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        var viewer = await AuthTestHelpers.RegisterWithResponseAsync(client, "viewer", "Viewer");
        await AuthTestHelpers.JoinSessionAsync(client, viewer.AccessToken, session.InviteCode, displayName: "Trail Viewer");

        var listResponse = await SendWithUserTokenAsync(
            client,
            HttpMethod.Get,
            $"/sessions/{session.SessionId}/members",
            owner.AccessToken);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var members = await listResponse.Content.ReadFromJsonAsync<List<SessionMember>>();
        Assert.NotNull(members);
        Assert.Contains(members, member => member.UserId == owner.User.UserId && member.Role == "owner");
        Assert.Contains(members, member => member.UserId == viewer.User.UserId && member.Role == "viewer");

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{session.SessionId}/members/{viewer.User.UserId}/role",
            owner.AccessToken,
            new { role = "runner" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var promoted = await response.Content.ReadFromJsonAsync<SessionMember>();
        Assert.NotNull(promoted);
        Assert.Equal(viewer.User.UserId, promoted.UserId);
        Assert.Equal("runner", promoted.Role);
        Assert.Equal("Trail Viewer", promoted.DisplayName);

        var writeAttempt = await LocationsApiTests.PostLocationAsync(
            client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            viewer.AccessToken);

        Assert.Equal(HttpStatusCode.OK, writeAttempt.StatusCode);
    }

    [Fact]
    public async Task UpdateSessionMemberRole_WithViewerToken_ReturnsForbidden()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var viewer = await AuthTestHelpers.RegisterWithResponseAsync(client, "viewer", "Viewer");
        await AuthTestHelpers.JoinSessionAsync(client, viewer.AccessToken, session.InviteCode);

        var response = await SendWithUserTokenAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{session.SessionId}/members/{viewer.User.UserId}/role",
            viewer.AccessToken,
            new { role = "runner" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSessionMemberRole_WithAdminBearerToken_PromotesViewerToRunner()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var viewer = await AuthTestHelpers.RegisterWithResponseAsync(client, "viewer", "Viewer");
        await AuthTestHelpers.JoinSessionAsync(client, viewer.AccessToken, session.InviteCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/sessions/{session.SessionId}/members/{viewer.User.UserId}/role")
        {
            Content = JsonContent.Create(new { role = "runner" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var promoted = await response.Content.ReadFromJsonAsync<SessionMember>();
        Assert.NotNull(promoted);
        Assert.Equal("runner", promoted.Role);
    }

    [Fact]
    public async Task JoinSession_WithExistingRunnerMembership_PreservesRunnerRole()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterWithResponseAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner.AccessToken);
        var runner = await AuthTestHelpers.RegisterWithResponseAsync(client, "runner", "Runner");
        await AuthTestHelpers.JoinSessionAsync(client, runner.AccessToken, session.InviteCode);
        await SendWithUserTokenAsync(
            client,
            HttpMethod.Post,
            $"/sessions/{session.SessionId}/members/{runner.User.UserId}/role",
            owner.AccessToken,
            new { role = "runner" });

        var joined = await AuthTestHelpers.JoinSessionAsync(
            client,
            runner.AccessToken,
            session.InviteCode,
            displayName: "Still Runner");

        Assert.Equal("runner", joined.Role);
        Assert.Equal("Still Runner", joined.DisplayName);
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

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/older-session-id/recording",
            NdjsonLine("Alice", "older-session-id", timestampSeconds: 1));
        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/newer-session-id/recording",
            NdjsonLine("Bob", "newer-session-id", timestampSeconds: 2));

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessions = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Equal(new[] { "newer-session-id", "older-session-id" }, sessions);
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
            "/sessions/some-session-id/recording",
            new StringContent(NdjsonLine("Alice", "some-session-id"), Encoding.UTF8, "application/x-ndjson"));

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
            "/sessions/some-session-id/recording",
            NdjsonLine("Alice", "some-session-id"),
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
            "/sessions/some-session-id/recording",
            "{");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithInvalidLocation_ReturnsBadRequestAndPreservesExistingRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/some-session-id/recording",
            NdjsonLine("Bob", "some-session-id"));

        var invalid = JsonSerializer.Serialize(
            LocationsApiTests.TestLocation("Alice", "some-session-id") with { Latitude = 91 });

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/some-session-id/recording",
            invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/some-session-id/recording");
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
            sessionId = "some-session-id",
            latitude = -41.17,
            longitude = 174.7762,
            heading = 270.5,
        });

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/some-session-id/recording",
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
            "/sessions/some-session-id/recording",
            "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadRecording_WithAdminBearerToken_ReplacesRecordingAndListsSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/target-session-id/recording",
            NdjsonLine("Bob", "target-session-id"));

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/target-session-id/recording",
            NdjsonLine("Alice", "ignored-session-id"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("target-session-id", body.RootElement.GetProperty("sessionId").GetString());

        var sessionsResponse = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions");
        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(sessions);
        Assert.Contains("target-session-id", sessions);

        var recording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/target-session-id/recording");
        var line = Assert.Single((await recording.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var uploaded = JsonDocument.Parse(line);
        Assert.Equal("Alice", uploaded.RootElement.GetProperty("runnerName").GetString());
    }

    [Fact]
    public async Task DeleteRecording_RequiresAdminBearerTokenAndRemovesRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/some-session-id/recording",
            NdjsonLine("Alice", "some-session-id"));

        var unauthorized = await client.DeleteAsync("/sessions/some-session-id/recording");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var deleted = await SendWithAdminBearerAsync(client, HttpMethod.Delete, "/sessions/some-session-id/recording");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var download = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/some-session-id/recording");
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

        await SendWithAdminBearerAsync(client, HttpMethod.Post, "/sessions/source-session-id/recording",
            NdjsonLine("Alice", "source-session-id"));

        var response = await SendWithAdminBearerAsync(
            client,
            HttpMethod.Post,
            "/sessions/target-session-id/merge-from/source-session-id");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("source-session-id", body.RootElement.GetProperty("sourceId").GetString());
        Assert.Equal("target-session-id", body.RootElement.GetProperty("targetId").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("recordsMerged").GetInt32());

        var sourceRecording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/source-session-id/recording");
        Assert.Equal(HttpStatusCode.NotFound, sourceRecording.StatusCode);

        var targetRecording = await SendWithAdminBearerAsync(client, HttpMethod.Get, "/sessions/target-session-id/recording");
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
            "/sessions/target-session-id/merge-from/missing-session-id");

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
