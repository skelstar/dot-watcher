using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// The admin page's Groups tab (/admin/groups): list groups and change them without being the organiser.
public class AdminGroupsTests
{
    private const double Lat = -36.8500;
    private const double Lon = 174.7600;
    private const string Admin = "test-token";

    private static DotWatcherApiFactory GroupsFactory() => new() { EnabledForAll = ["events", "groups", "create-groups"] };

    [Fact]
    public async Task List_ShowsAGroup_WithItsOrganiserAndSettings()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri, entryCode: "K7Q4");

        var group = await GetGroupAsync(client, created.SessionId);

        Assert.Equal("Hunua 50k", group.SessionName);
        Assert.Equal("kiri", group.OwnerUsername);
        Assert.Equal("Kiri", group.OwnerDisplayName);
        Assert.Equal(1, group.MemberCount);
        Assert.Equal("active", group.Status);
        Assert.Equal(24, group.LengthHours);
        Assert.True(group.Nearby);
        Assert.Equal(100, group.NearbyRadiusMeters);
        Assert.Equal(Lat, group.JoinLatitude);
        Assert.Equal("K7Q4", group.EntryCode);
    }

    [Fact]
    public async Task List_LeavesOutTheCasualSession()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();

        var groups = await (await SendAsync(client, HttpMethod.Get, "/admin/groups", Admin))
            .Content.ReadFromJsonAsync<List<AdminGroupSummary>>();

        Assert.DoesNotContain(groups!, g => g.SessionId == SessionStore.CasualSessionId);
    }

    [Fact]
    public async Task Patch_ChangesOnlyWhatIsGiven_AndThePhonesSeeTheNewEndTime()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri, entryCode: "K7Q4");

        var response = await SendAsync(client, HttpMethod.Patch, $"/admin/groups/{created.SessionId}", Admin,
            new { sessionName = "Hunua 100k", lengthHours = 48, nearbyRadiusMeters = 500 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var group = (await response.Content.ReadFromJsonAsync<AdminGroupSummary>())!;

        Assert.Equal("Hunua 100k", group.SessionName);
        Assert.Equal(48, group.LengthHours);
        Assert.Equal(500, group.NearbyRadiusMeters);
        Assert.Equal("K7Q4", group.EntryCode);
        Assert.True(group.Nearby);

        var mine = Assert.Single(await MySessionsAsync(client, kiri));
        Assert.Equal("Hunua 100k", mine.SessionName);
        Assert.Equal(DateTimeOffset.Parse(group.EndsAt), DateTimeOffset.Parse(mine.EndsAt!));
    }

    [Fact]
    public async Task Patch_EntryCode_SetsAndClears()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri);
        var path = $"/admin/groups/{created.SessionId}";

        var set = await SendAsync(client, HttpMethod.Patch, path, Admin, new { entryCode = " ab12 " });
        Assert.Equal("AB12", (await set.Content.ReadFromJsonAsync<AdminGroupSummary>())!.EntryCode);

        var cleared = await SendAsync(client, HttpMethod.Patch, path, Admin, new { entryCode = "" });
        Assert.Null((await cleared.Content.ReadFromJsonAsync<AdminGroupSummary>())!.EntryCode);
    }

    [Theory]
    [InlineData("""{"sessionName":"   "}""")]
    [InlineData("""{"lengthHours":0}""")]
    [InlineData("""{"lengthHours":241}""")]
    [InlineData("""{"nearbyRadiusMeters":75}""")]
    [InlineData("""{"joinLatitude":-36.85}""")]
    [InlineData("""{"joinLatitude":95,"joinLongitude":174.76}""")]
    [InlineData("""{"entryCode":"K7"}""")]
    public async Task Patch_RefusesBadValues(string body)
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/admin/groups/{created.SessionId}")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Patch_NearbyOn_NeedsAJoinLocation()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await AuthTestHelpers.CreateSessionAsync(client, kiri);
        var path = $"/admin/groups/{created.SessionId}";

        var refused = await SendAsync(client, HttpMethod.Patch, path, Admin, new { nearby = true });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var accepted = await SendAsync(client, HttpMethod.Patch, path, Admin,
            new { nearby = true, joinLatitude = Lat, joinLongitude = Lon });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var group = (await accepted.Content.ReadFromJsonAsync<AdminGroupSummary>())!;
        Assert.True(group.Nearby);
        Assert.Equal(Lon, group.JoinLongitude);
    }

    [Fact]
    public async Task End_ThenReopen_ThenAddAMemberBack()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri);
        var path = $"/admin/groups/{created.SessionId}";

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"{path}/end", Admin)).StatusCode);
        var ended = await GetGroupAsync(client, created.SessionId);
        Assert.Equal("ended", ended.Status);
        Assert.Equal(0, ended.MemberCount);
        Assert.Empty(await MySessionsAsync(client, kiri));

        // An ended group can't take members until it's reopened.
        var tooSoon = await SendAsync(client, HttpMethod.Post, $"{path}/members", Admin, new { username = "kiri" });
        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"{path}/reopen", Admin)).StatusCode);
        Assert.Equal("active", (await GetGroupAsync(client, created.SessionId)).Status);

        var added = await SendAsync(client, HttpMethod.Post, $"{path}/members", Admin, new { username = " KIRI " });
        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Single(await MySessionsAsync(client, kiri));
    }

    [Fact]
    public async Task AddMember_WithNoDistanceOrCodeCheck_ThenRemove()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var sam = await AuthTestHelpers.RegisterWithResponseAsync(client, "sam", "Sam");
        var created = await CreateNearbyAsync(client, kiri, entryCode: "K7Q4");
        var path = $"/admin/groups/{created.SessionId}";

        var added = await SendAsync(client, HttpMethod.Post, $"{path}/members", Admin,
            new { username = "sam", displayName = "Sammy" });
        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);

        var members = await (await SendAsync(client, HttpMethod.Get, $"{path}/members", Admin))
            .Content.ReadFromJsonAsync<List<AdminGroupMember>>();
        var samInGroup = Assert.Single(members!, m => m.Username == "sam");
        Assert.Equal("Sammy", samInGroup.DisplayName);
        Assert.Null(samInGroup.LeftAt);
        var theirs = Assert.Single(await MySessionsAsync(client, sam.AccessToken));
        Assert.Equal("Kiri", theirs.OwnerDisplayName);

        var removed = await SendAsync(client, HttpMethod.Delete, $"{path}/members/{sam.User.UserId}", Admin);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await MySessionsAsync(client, sam.AccessToken));
        // Kiri is still a runner in it, so it carries on.
        Assert.Equal("active", (await GetGroupAsync(client, created.SessionId)).Status);
    }

    [Fact]
    public async Task AddMember_UnknownAccountOrGroup_IsNotFound()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post,
            $"/admin/groups/{created.SessionId}/members", Admin, new { username = "nobody" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post,
            "/admin/groups/no-such-group/members", Admin, new { username = "kiri" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post,
            $"/admin/groups/{SessionStore.CasualSessionId}/end", Admin)).StatusCode);
    }

    [Fact]
    public async Task EveryEndpoint_NeedsTheAdminToken()
    {
        using var factory = GroupsFactory();
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var created = await CreateNearbyAsync(client, kiri);
        var path = $"/admin/groups/{created.SessionId}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/admin/groups", kiri)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"{path}/members", kiri)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Patch, path, kiri, new { lengthHours = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, $"{path}/end", kiri)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, $"{path}/reopen", kiri)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, $"{path}/members", kiri, new { username = "kiri" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Delete, $"{path}/members/x", kiri)).StatusCode);
    }

    private static async Task<AdminGroupSummary> GetGroupAsync(HttpClient client, string sessionId)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/admin/groups", Admin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var groups = await response.Content.ReadFromJsonAsync<List<AdminGroupSummary>>();
        return Assert.Single(groups!, g => g.SessionId == sessionId);
    }

    private static async Task<List<SessionMembership>> MySessionsAsync(HttpClient client, string token) =>
        await (await SendAsync(client, HttpMethod.Get, "/me/sessions", token))
            .Content.ReadFromJsonAsync<List<SessionMembership>>() ?? [];

    private static async Task<SessionMembership> CreateNearbyAsync(HttpClient client, string token, string? entryCode = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/sessions", token, new
        {
            sessionName = "Hunua 50k",
            nearby = true,
            latitude = Lat,
            longitude = Lon,
            accuracy = 8,
            entryCode,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
