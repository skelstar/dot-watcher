using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class DemoSessionTests
{
    // Must match server/appsettings.json's DemoSession:InviteCode - DotWatcherApiFactory doesn't
    // override that section, so every test run seeds the same reserved demo session.
    private const string DemoInviteCode = "ABC123";

    [Fact]
    public async Task JoinDemoSession_TwoRunners_EachSeesOnlySelfAndDw()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var aliceToken = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var alice = await AuthTestHelpers.JoinSessionAsync(client, aliceToken, DemoInviteCode, role: "runner");

        var bobToken = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var bob = await AuthTestHelpers.JoinSessionAsync(client, bobToken, DemoInviteCode, role: "runner");

        await LocationsApiTests.PostLocationAsync(
            client, LocationsApiTests.TestLocation("Ignored", alice.SessionId), aliceToken);
        await LocationsApiTests.PostLocationAsync(
            client, LocationsApiTests.TestLocation("Ignored", bob.SessionId), bobToken);

        var aliceNames = await GetRunnerNamesAsync(client, alice.SessionId, aliceToken);
        Assert.Contains("Alice", aliceNames);
        Assert.Contains("DW", aliceNames);
        Assert.DoesNotContain("Bob", aliceNames);

        var bobNames = await GetRunnerNamesAsync(client, bob.SessionId, bobToken);
        Assert.Contains("Bob", bobNames);
        Assert.Contains("DW", bobNames);
        Assert.DoesNotContain("Alice", bobNames);
    }

    [Fact]
    public async Task LeaveDemoSession_DoesNotArchiveSession()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var token = await AuthTestHelpers.RegisterAsync(client, "carol", "Carol");
        var membership = await AuthTestHelpers.JoinSessionAsync(client, token, DemoInviteCode, role: "runner");

        using var leaveRequest = AuthTestHelpers.WithUserToken(
            HttpMethod.Delete, $"/me/sessions/{membership.SessionId}/membership", token);
        var leaveResponse = await client.SendAsync(leaveRequest);
        Assert.Equal(HttpStatusCode.NoContent, leaveResponse.StatusCode);

        using var rejoinRequest = new HttpRequestMessage(HttpMethod.Post, $"/session-invites/{DemoInviteCode}/join")
        {
            Content = JsonContent.Create(new { role = "runner", displayName = (string?)null }),
        };
        rejoinRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rejoinResponse = await client.SendAsync(rejoinRequest);

        Assert.Equal(HttpStatusCode.OK, rejoinResponse.StatusCode);
    }

    [Fact]
    public async Task PostLocation_InDemoSession_DoesNotPersistRecording()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var token = await AuthTestHelpers.RegisterAsync(client, "dave", "Dave");
        var membership = await AuthTestHelpers.JoinSessionAsync(client, token, DemoInviteCode, role: "runner");

        var postResponse = await LocationsApiTests.PostLocationAsync(
            client, LocationsApiTests.TestLocation("Ignored", membership.SessionId), token);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var recordingResponse = await client.GetAsync($"/session-invites/{DemoInviteCode}/recording");

        Assert.Equal(HttpStatusCode.NotFound, recordingResponse.StatusCode);
    }

    private static async Task<List<string?>> GetRunnerNamesAsync(HttpClient client, string sessionId, string accessToken)
    {
        var response = await LocationsApiTests.SendWithUserTokenAsync(
            client, HttpMethod.Get, $"/locations/{sessionId}", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var positions = await response.Content.ReadFromJsonAsync<List<List<RunnerPosition>>>();
        Assert.NotNull(positions);

        return positions.SelectMany(runnerPositions => runnerPositions)
            .Select(position => position.RunnerName)
            .ToList();
    }
}
