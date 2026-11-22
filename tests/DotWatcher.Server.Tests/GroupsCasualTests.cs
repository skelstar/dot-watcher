using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Groups, step 1 (see .ai/plans/go-and-groups.md): with the "groups" feature, people who share an
// active named session see each other by name in casual, as crew do, at any distance.
public class GroupsCasualTests
{
    private const double HereLatitude = -41.17;
    private const double HereLongitude = 174.7762;
    // About 55 km north: far outside the 5 km nearby radius, so only a name can make them visible.
    private const double FarLatitude = -40.67;

    private static readonly DateTimeOffset Created = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    private static DotWatcherApiFactory GroupsFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual, Features.Groups] };

    // ---- the pure rule ---------------------------------------------------------------------------

    [Fact]
    public void IsGroupActive_WithinItsEventLength() =>
        Assert.True(SessionStore.IsGroupActive(Created, 4, Created.AddHours(3.9)));

    [Fact]
    public void IsGroupActive_EndsAtItsEventLength()
    {
        Assert.False(SessionStore.IsGroupActive(Created, 4, Created.AddHours(4)));
        Assert.False(SessionStore.IsGroupActive(Created, 4, Created.AddHours(5)));
    }

    [Fact]
    public void IsGroupActive_WithNoLengthSet_LastsTwentyFourHours()
    {
        Assert.True(SessionStore.IsGroupActive(Created, null, Created.AddHours(23.9)));
        Assert.False(SessionStore.IsGroupActive(Created, null, Created.AddHours(24)));
    }

    // ---- through the API -------------------------------------------------------------------------

    [Fact]
    public async Task GroupMembers_SeeEachOtherByTheirGroupName_AtAnyDistance()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, _) = await GroupOfTwoAsync(client);
        await GoAsync(client, bob.AccessToken, FarLatitude);
        await GoAsync(client, alice.AccessToken, HereLatitude);

        var aliceSees = await GetLiveAsync(client, alice.AccessToken);
        var bobSees = await GetLiveAsync(client, bob.AccessToken);

        Assert.Contains("Bobby", aliceSees.Participants);
        Assert.Contains("Ali", bobSees.Participants);
        Assert.Empty(aliceSees.Nearby);
    }

    [Fact]
    public async Task WithoutTheFeature_GroupMembersAreNotNamed()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Crew, Features.Casual] };
        using var client = factory.CreateClient();
        var (alice, bob, _) = await GroupOfTwoAsync(client);
        await GoAsync(client, bob.AccessToken, FarLatitude);

        var live = await GetLiveAsync(client, alice.AccessToken);

        Assert.Empty(live.Participants);
    }

    [Fact]
    public async Task WithTheFeatureSwitchedOff_GroupMembersAreNotNamed()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, _) = await GroupOfTwoAsync(client);
        await GoAsync(client, bob.AccessToken, FarLatitude);
        using (var off = new HttpRequestMessage(HttpMethod.Put, $"/admin/features/{Features.Groups}")
        {
            Content = JsonContent.Create(new { enabled = false }),
        })
        {
            off.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
            Assert.True((await client.SendAsync(off)).IsSuccessStatusCode);
        }

        var live = await GetLiveAsync(client, alice.AccessToken);

        Assert.Empty(live.Participants);
    }

    [Fact]
    public async Task MemberWhoLeft_IsNoLongerNamed_EitherWay()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, session) = await GroupOfTwoAsync(client);
        await GoAsync(client, alice.AccessToken, HereLatitude);
        await GoAsync(client, bob.AccessToken, FarLatitude);

        var leave = await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{session.SessionId}/membership", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        Assert.DoesNotContain("Bobby", (await GetLiveAsync(client, alice.AccessToken)).Participants);
        Assert.DoesNotContain("Ali", (await GetLiveAsync(client, bob.AccessToken)).Participants);
    }

    [Fact]
    public async Task EndedSession_NoLongerCounts()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, session) = await GroupOfTwoAsync(client);
        await GoAsync(client, bob.AccessToken, FarLatitude);

        var end = await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", alice.AccessToken);
        Assert.True(end.IsSuccessStatusCode);

        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Participants);
    }

    [Fact]
    public async Task BlockedMember_IsNeverNamed()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, _) = await GroupOfTwoAsync(client);
        await GoAsync(client, bob.AccessToken, FarLatitude);

        var block = await SendAsync(client, HttpMethod.Post, $"/me/blocks/{bob.User.UserId}", alice.AccessToken);
        Assert.True(block.IsSuccessStatusCode);

        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Participants);
    }

    [Fact]
    public async Task MyCrewSwitchedOff_HidesGroupMembersToo()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, _) = await GroupOfTwoAsync(client);
        await GoAsync(client, bob.AccessToken, FarLatitude);
        var settings = await SendAsync(client, HttpMethod.Put, "/me/casual/settings", bob.AccessToken, new { visibleToCrew = false });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        Assert.Empty((await GetLiveAsync(client, alice.AccessToken)).Participants);
    }

    [Fact]
    public async Task CrewInTheSameGroup_KeepTheirCrewLabel()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var (alice, bob, _) = await GroupOfTwoAsync(client);
        var code = await CrewApiTests.CreateCodeAsync(client, alice.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        await GoAsync(client, bob.AccessToken, FarLatitude);

        var live = await GetLiveAsync(client, alice.AccessToken);

        // Bob's crew label is his account's display name ("BO"), not the name he uses in the group.
        Assert.Equal(new[] { "BO" }, live.Participants);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    // Alice creates a named session as "Ali"; Bob joins it as a runner called "Bobby". They aren't crew.
    private static async Task<(AuthResponse Alice, AuthResponse Bob, SessionMembership Session)> GroupOfTwoAsync(HttpClient client)
    {
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice.AccessToken, displayName: "Ali");
        var joined = await AuthTestHelpers.JoinSessionAsync(client, bob.AccessToken, session.InviteCode, role: "runner", displayName: "Bobby");
        Assert.Equal(session.SessionId, joined.SessionId);
        return (alice, bob, session);
    }

    private static async Task GoAsync(HttpClient client, string accessToken, double latitude)
    {
        var start = await SendAsync(client, HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate(
                RunnerName: "ignored",
                SessionId: SessionStore.CasualSessionId,
                Latitude: latitude,
                Longitude: HereLongitude,
                Heading: 90,
                Timestamp: DateTimeOffset.UtcNow),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    private static async Task<CasualLiveResponse> GetLiveAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/casual/live", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CasualLiveResponse>())!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
