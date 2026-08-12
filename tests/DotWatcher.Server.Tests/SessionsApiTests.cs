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
    public async Task GetLocationsByInviteCode_WhenPostLandedOnADifferentPod_StillReturnsThePosition()
    {
        // Simulates Staging and Production after the shared-Postgres cutover: two separate server
        // processes - each with its own private in-memory `_sessions` cache - pointed at the same
        // database. A phone's POST /location can land on either pod; a viewer polling the other
        // pod must still see the position via the shared database, not just the pod that received
        // it (this reproduces a real bug: dot-watcher.skelstar.io/api/session-invites/{code}/locations
        // returned [] for a session whose only posts had landed on the staging pod).
        using var podA = new DotWatcherApiFactory();
        using var podB = new DotWatcherApiFactory(podA.Schema);
        using var clientA = podA.CreateClient();
        using var clientB = podB.CreateClient();

        var ownerToken = await AuthTestHelpers.RegisterAsync(clientA, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(clientA, ownerToken);

        await LocationsApiTests.PostLocationAsync(
            clientA,
            LocationsApiTests.TestLocation("Owner", session.SessionId),
            ownerToken);

        var response = await clientB.GetAsync($"/session-invites/{session.InviteCode}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var positions = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(positions);
        Assert.Contains(positions, group => group.Any(p => p.RunnerName == "Owner"));
    }

    [Fact]
    public async Task GetLocationsByInviteCode_WhenPostLandedOnADifferentPod_IncludesNextExpectedAtAndIsUltraConstrained()
    {
        // Same cross-pod scenario as the test above, but asserting the two fields that only
        // survive the Postgres fallback (LoadLatestPositionsByRunner) since 2026-08-12 - before
        // that, this pod's read would silently default both to null/false.
        using var podA = new DotWatcherApiFactory();
        using var podB = new DotWatcherApiFactory(podA.Schema);
        using var clientA = podA.CreateClient();
        using var clientB = podB.CreateClient();

        var ownerToken = await AuthTestHelpers.RegisterAsync(clientA, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(clientA, ownerToken);
        var location = LocationsApiTests.TestLocation("Owner", session.SessionId);
        var nextExpectedAt = location.Timestamp!.Value.AddSeconds(90);

        await LocationsApiTests.PostLocationAsync(
            clientA,
            location with { NextExpectedAt = nextExpectedAt, IsUltraConstrained = true },
            ownerToken);

        var response = await clientB.GetAsync($"/session-invites/{session.InviteCode}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var positions = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(positions);
        var position = Assert.Single(positions.SelectMany(group => group));
        Assert.True(position.IsUltraConstrained);
        Assert.Equal(nextExpectedAt, position.NextExpectedAt);
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
    public async Task GetRecordingByInviteCode_IncludesNextExpectedAtAndIsUltraConstrained()
    {
        // GetRecordingAsNdjson (no since/until) is backed by LoadUpdatesByTimestamp, one of the
        // Postgres reads that silently dropped both fields before 2026-08-12 - this is the actual
        // playback path a viewer scrubbing into history hits, unlike the in-memory-backed
        // /locations tests above.
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "isla", "Isla");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);
        var location = LocationsApiTests.TestLocation("Ignored", session.SessionId);
        var nextExpectedAt = location.Timestamp!.Value.AddSeconds(90);

        await LocationsApiTests.PostLocationAsync(
            client,
            location with { NextExpectedAt = nextExpectedAt, IsUltraConstrained = true },
            token);

        var response = await client.GetAsync($"/session-invites/{session.InviteCode}/recording");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.True(json.RootElement.GetProperty("isUltraConstrained").GetBoolean());
        Assert.Equal(nextExpectedAt, json.RootElement.GetProperty("nextExpectedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task GetRecordingByInviteCode_WithGapFromEarlierRun_OnlyReturnsLatestRun()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "carol", "Carol");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var earlierRun = LocationsApiTests.TestLocation("Ignored", session.SessionId, latitude: -33.8000) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 2, 0, 0, TimeSpan.Zero),
        };
        var latestRun = LocationsApiTests.TestLocation("Ignored", session.SessionId, latitude: -33.8688) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 23, 0, TimeSpan.Zero),
        };
        await LocationsApiTests.PostLocationAsync(client, earlierRun, token);
        await LocationsApiTests.PostLocationAsync(client, latestRun, token);

        var response = await client.GetAsync($"/session-invites/{session.InviteCode}/recording");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.Equal(-33.8688, json.RootElement.GetProperty("latitude").GetDouble());
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
    public async Task GetRecordingByInviteCode_WithSinceUntil_ReturnsOnlyPointsInWindow()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "dana", "Dana");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var first = LocationsApiTests.TestLocation("Dana", session.SessionId, latitude: -33.80) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 0, 0, TimeSpan.Zero),
        };
        var second = LocationsApiTests.TestLocation("Dana", session.SessionId, latitude: -33.81) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 10, 0, TimeSpan.Zero),
        };
        var third = LocationsApiTests.TestLocation("Dana", session.SessionId, latitude: -33.82) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 20, 0, TimeSpan.Zero),
        };
        await LocationsApiTests.PostLocationAsync(client, first, token);
        await LocationsApiTests.PostLocationAsync(client, second, token);
        await LocationsApiTests.PostLocationAsync(client, third, token);

        var since = Uri.EscapeDataString("2024-11-15T09:05:00Z");
        var until = Uri.EscapeDataString("2024-11-15T09:15:00Z");
        var response = await client.GetAsync(
            $"/session-invites/{session.InviteCode}/recording?since={since}&until={until}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.Equal(-33.81, json.RootElement.GetProperty("latitude").GetDouble());
    }

    [Fact]
    public async Task GetRecordingByInviteCode_WithSinceUntil_IncludesNextExpectedAtAndIsUltraConstrained()
    {
        // GetRecordingWindowAsNdjson is the scrubber's actual data source while replaying
        // (see useSessionTimeline.ts's fetchWindow) - the read path that matters most for the
        // playback icon this change exists for.
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "jonah", "Jonah");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);
        var location = LocationsApiTests.TestLocation("Ignored", session.SessionId) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 10, 0, TimeSpan.Zero),
        };
        var nextExpectedAt = location.Timestamp!.Value.AddSeconds(90);

        await LocationsApiTests.PostLocationAsync(
            client,
            location with { NextExpectedAt = nextExpectedAt, IsUltraConstrained = true },
            token);

        var since = Uri.EscapeDataString("2024-11-15T09:05:00Z");
        var until = Uri.EscapeDataString("2024-11-15T09:15:00Z");
        var response = await client.GetAsync(
            $"/session-invites/{session.InviteCode}/recording?since={since}&until={until}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(line);
        Assert.True(json.RootElement.GetProperty("isUltraConstrained").GetBoolean());
        Assert.Equal(nextExpectedAt, json.RootElement.GetProperty("nextExpectedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task DownloadRecording_WithSinceBeforeRunStart_ClampsToRunStart()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var earlierRun = NdjsonLineAt("Erin", SomeSessionId, new DateTimeOffset(2024, 11, 15, 2, 0, 0, TimeSpan.Zero));
        var runStart = NdjsonLineAt("Erin", SomeSessionId, new DateTimeOffset(2024, 11, 15, 9, 0, 0, TimeSpan.Zero));
        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{SomeSessionId}/recording", earlierRun);
        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{SomeSessionId}/recording", runStart);

        var since = Uri.EscapeDataString("2024-11-15T00:00:00Z");
        var response = await SendWithAdminBearerAsync(
            client, HttpMethod.Get, $"/sessions/{SomeSessionId}/recording?since={since}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // Only the most recent run's point is returned even though `since` asked for everything,
        // because the windowed query is clamped to the current run's start.
        var line = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var json = JsonDocument.Parse(line);
        Assert.Equal("2024-11-15T09:00:00+00:00", json.RootElement.GetProperty("timestamp").GetString());
    }

    [Fact]
    public async Task UploadRecording_PreservesNextExpectedAtAndIsUltraConstrained()
    {
        // Covers SaveRecording/ParseRecordingLine - the re-import path scripts/import-session.sh
        // uses. Both fields must survive an upload → download round trip, not just a live POST.
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var timestamp = new DateTimeOffset(2024, 11, 15, 9, 0, 0, TimeSpan.Zero);
        var line = JsonSerializer.Serialize(LocationsApiTests.TestLocation("Kit", SomeSessionId) with
        {
            Timestamp = timestamp,
            NextExpectedAt = timestamp.AddSeconds(90),
            IsUltraConstrained = true,
        });

        await SendWithAdminBearerAsync(client, HttpMethod.Post, $"/sessions/{SomeSessionId}/recording", line);

        var response = await SendWithAdminBearerAsync(client, HttpMethod.Get, $"/sessions/{SomeSessionId}/recording");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var recordedLine = Assert.Single(body.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        using var json = JsonDocument.Parse(recordedLine);
        Assert.True(json.RootElement.GetProperty("isUltraConstrained").GetBoolean());
        Assert.Equal(timestamp.AddSeconds(90), json.RootElement.GetProperty("nextExpectedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task GetRecordingMeta_ReturnsRunStartAndLatestTimestamp()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "finn", "Finn");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var start = LocationsApiTests.TestLocation("Finn", session.SessionId) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 0, 0, TimeSpan.Zero),
        };
        var latest = LocationsApiTests.TestLocation("Finn", session.SessionId) with
        {
            Timestamp = new DateTimeOffset(2024, 11, 15, 9, 30, 0, TimeSpan.Zero),
        };
        await LocationsApiTests.PostLocationAsync(client, start, token);
        await LocationsApiTests.PostLocationAsync(client, latest, token);

        var response = await SendWithUserTokenAsync(
            client, HttpMethod.Get, $"/sessions/{session.SessionId}/recording/meta", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var meta = await response.Content.ReadFromJsonAsync<RecordingMeta>();
        Assert.NotNull(meta);
        Assert.Equal(start.Timestamp, meta!.RunStartTimestamp);
        Assert.Equal(latest.Timestamp, meta.LatestTimestamp);
    }

    [Fact]
    public async Task GetRecordingMetaByInviteCode_WithUnknownInvite_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/session-invites/NOPE99/recording/meta");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRecordingMeta_WithNoRecording_ReturnsNotFound()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "gwen", "Gwen");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await SendWithUserTokenAsync(
            client, HttpMethod.Get, $"/sessions/{session.SessionId}/recording/meta", token);

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

    private static string NdjsonLineAt(string runnerName, string sessionId, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(LocationsApiTests.TestLocation(runnerName, sessionId) with
        {
            Timestamp = timestamp,
        });
}
