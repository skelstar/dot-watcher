using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Wire contract for the iOS/Android/web clients, which hardcode these JSON field names in their
// own languages. Unlike the rest of this suite, these tests build requests and read responses as
// raw JSON rather than the shared C# record types, so a renamed/removed property fails here
// instead of silently recompiling on both ends of a round-trip.
public class ContractTests
{
    [Fact]
    public async Task Register_ResponseContract_HasExpectedJsonFields()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            username = "alice",
            password = "correct-horse-password",
            displayName = "Alice",
        });

        var root = await ParseAsync(response);

        Assert.Equal(JsonValueKind.String, root.GetProperty("accessToken").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("expiresAt").ValueKind);

        var user = root.GetProperty("user");
        Assert.Equal(JsonValueKind.String, user.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.String, user.GetProperty("username").ValueKind);
        Assert.Equal(JsonValueKind.String, user.GetProperty("displayName").ValueKind);
    }

    [Fact]
    public async Task CreateSession_ResponseContract_HasExpectedJsonFields()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = JsonContent.Create(new { sessionName = "sunset23", displayName = "Trail Alice" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        AssertSessionMembershipContract(await ParseAsync(response));
    }

    [Fact]
    public async Task JoinSession_ResponseContract_HasExpectedJsonFields()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var ownerToken = await AuthTestHelpers.RegisterAsync(client, "owner", "Owner");
        var session = await AuthTestHelpers.CreateSessionAsync(client, ownerToken);
        var viewerToken = await AuthTestHelpers.RegisterAsync(client, "viewer", "Viewer");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/session-invites/{session.InviteCode}/join")
        {
            Content = JsonContent.Create(new { role = "viewer", displayName = "Roadside Viewer" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        var response = await client.SendAsync(request);

        AssertSessionMembershipContract(await ParseAsync(response));
    }

    [Fact]
    public async Task PostLocation_WithHandWrittenJsonFieldNames_IsAccepted()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        var response = await PostLiteralLocationAsync(client, token, session.SessionId, includeHeading: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestPositions_ResponseContract_HasExpectedJsonFieldsAndPreservesNullHeading()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        await PostLiteralLocationAsync(client, token, session.SessionId, includeHeading: false);

        var response = await LocationsApiTests.SendWithUserTokenAsync(
            client, HttpMethod.Get, $"/locations/{session.SessionId}", token);

        var root = await ParseAsync(response);
        var position = root.EnumerateArray().SelectMany(group => group.EnumerateArray()).Single();

        Assert.Equal(JsonValueKind.String, position.GetProperty("runnerName").ValueKind);
        Assert.Equal(JsonValueKind.Number, position.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Number, position.GetProperty("longitude").ValueKind);
        Assert.True(position.TryGetProperty("heading", out var heading), "Expected a 'heading' key even when null.");
        Assert.Equal(JsonValueKind.Null, heading.ValueKind);
        Assert.Equal(JsonValueKind.String, position.GetProperty("timestamp").ValueKind);
    }

    [Fact]
    public async Task GetRecordingMeta_ResponseContract_HasExpectedJsonFields()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);
        await LocationsApiTests.PostLocationAsync(
            client, LocationsApiTests.TestLocation("Ignored", session.SessionId), token);

        var response = await LocationsApiTests.SendWithUserTokenAsync(
            client, HttpMethod.Get, $"/sessions/{session.SessionId}/recording/meta", token);

        var root = await ParseAsync(response);

        Assert.True(root.TryGetProperty("runStartTimestamp", out var runStart));
        Assert.Equal(JsonValueKind.String, runStart.ValueKind);
        Assert.True(root.TryGetProperty("latestTimestamp", out var latest));
        Assert.Equal(JsonValueKind.String, latest.ValueKind);
    }

    private static void AssertSessionMembershipContract(JsonElement root)
    {
        Assert.Equal(JsonValueKind.String, root.GetProperty("sessionId").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("sessionName").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("inviteCode").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("displayName").ValueKind);
    }

    private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> PostLiteralLocationAsync(
        HttpClient client,
        string accessToken,
        string sessionId,
        bool includeHeading)
    {
        var json = includeHeading
            ? JsonSerializer.Serialize(new
            {
                runnerName = "Ignored",
                sessionId,
                latitude = -41.17,
                longitude = 174.7762,
                heading = 270.5,
                timestamp = "2024-11-15T09:23:00Z",
            })
            : JsonSerializer.Serialize(new
            {
                runnerName = "Ignored",
                sessionId,
                latitude = -41.17,
                longitude = 174.7762,
                timestamp = "2024-11-15T09:23:00Z",
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/location")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request);
    }
}
