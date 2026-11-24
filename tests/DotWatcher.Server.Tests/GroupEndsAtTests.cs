using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// When a group ends (see .ai/plans/go-and-groups.md, step 2): the session's creation time plus its event
// length, on the membership and, for accounts with "groups", on the nearby list.
public class GroupEndsAtTests
{
    private const double Lat = -36.8500;
    private const double Lon = 174.7600;
    private const double About100m = 0.0009;

    [Fact]
    public void GroupEndsAt_IsCreatedPlusTheEventLength_DefaultTwentyFourHours()
    {
        var created = new DateTimeOffset(2026, 11, 24, 21, 0, 0, TimeSpan.FromHours(13));

        Assert.Equal(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), SessionStore.GroupEndsAt(created, 4));
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero), SessionStore.GroupEndsAt(created, null));
        Assert.Equal(TimeSpan.Zero, SessionStore.GroupEndsAt(created, 4).Offset);
    }

    [Fact]
    public async Task Membership_CarriesEndsAt_OnCreate_Join_AndTheSessionList()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");

        var created = await AuthTestHelpers.CreateSessionAsync(client, owner, maxLengthHours: 4);
        AssertEndsAfter(created, hours: 4);

        var joined = await AuthTestHelpers.JoinSessionAsync(client, joiner, created.InviteCode, role: "runner");
        AssertEndsAfter(joined, hours: 4);

        var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", joiner);
        AssertEndsAfter(Assert.Single((await mine.Content.ReadFromJsonAsync<List<SessionMembership>>())!), hours: 4);
    }

    [Fact]
    public async Task Membership_WithNoLengthSet_EndsAfterTwentyFourHours()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        await AuthTestHelpers.CreateSessionAsync(client, owner);

        var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", owner);
        AssertEndsAfter(Assert.Single((await mine.Content.ReadFromJsonAsync<List<SessionMembership>>())!), hours: 24);
    }

    [Fact]
    public async Task NearbyList_SendsEndsAt_OnlyToGroupsAccounts()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var organiser = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var withGroups = await AuthTestHelpers.RegisterWithResponseAsync(client, "aroha", "AK");
        await GrantGroupsAsync(client, withGroups.User.UserId);
        var without = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await CreateNearbyAsync(client, organiser, maxLengthHours: 6);

        var item = Assert.Single((await ListAsync(client, withGroups.AccessToken)).EnumerateArray());
        var endsAt = DateTimeOffset.Parse(item.GetProperty("endsAt").GetString()!);
        Assert.Equal(DateTimeOffset.Parse(session.CreatedAt!).AddHours(6), endsAt);

        var plain = Assert.Single((await ListAsync(client, without)).EnumerateArray());
        Assert.False(plain.TryGetProperty("endsAt", out _));
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static void AssertEndsAfter(SessionMembership membership, int hours)
    {
        Assert.NotNull(membership.CreatedAt);
        Assert.NotNull(membership.EndsAt);
        Assert.Equal(DateTimeOffset.Parse(membership.CreatedAt!).AddHours(hours), DateTimeOffset.Parse(membership.EndsAt!));
    }

    private static async Task<SessionMembership> CreateNearbyAsync(HttpClient client, string token, int maxLengthHours)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/sessions", token, new
        {
            sessionName = "Hunua 50k",
            nearby = true,
            latitude = Lat,
            longitude = Lon,
            accuracy = 8,
            maxLengthHours,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionMembership>())!;
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string token)
    {
        var query = FormattableString.Invariant($"lat={Lat + 0.5 * About100m}&lon={Lon}&accuracy=5");
        var response = await SendAsync(client, HttpMethod.Get, $"/me/events/nearby?{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task GrantGroupsAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/admin/users/{userId}/features")
        {
            Content = JsonContent.Create(new { features = new[] { "groups" } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        Assert.True((await client.SendAsync(request)).IsSuccessStatusCode);
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
