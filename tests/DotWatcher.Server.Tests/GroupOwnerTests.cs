using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Who organised a group (see .ai/plans/go-and-groups.md, step 2): memberships say isOwner, and only the
// owner is told whether the session is shown nearby, so the phones can put the organiser's controls on the
// start screen without keeping that on the device.
public class GroupOwnerTests
{
    private const double Lat = -36.8500;
    private const double Lon = 174.7600;

    [Fact]
    public async Task Owner_IsOwner_WithNearby_OnCreateAndTheSessionList()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        var created = await SendAsync(client, HttpMethod.Post, "/sessions", owner, new
        {
            sessionName = "Hunua 50k",
            nearby = true,
            latitude = Lat,
            longitude = Lon,
            accuracy = 8,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using (var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            Assert.True(json.RootElement.GetProperty("isOwner").GetBoolean());
            Assert.True(json.RootElement.GetProperty("nearby").GetBoolean());
        }

        var mine = Assert.Single((await ListAsync(client, owner)).EnumerateArray());
        Assert.True(mine.GetProperty("isOwner").GetBoolean());
        Assert.True(mine.GetProperty("nearby").GetBoolean());
        Assert.Equal(100, mine.GetProperty("nearbyRadiusMeters").GetInt32());
    }

    [Fact]
    public async Task Owner_SeesTheNewRadius_AfterChangingIt()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var created = await SendAsync(client, HttpMethod.Post, "/sessions", owner, new
        {
            sessionName = "Hunua 50k",
            nearby = true,
            latitude = Lat,
            longitude = Lon,
            accuracy = 8,
        });
        var sessionId = (await created.Content.ReadFromJsonAsync<SessionMembership>())!.SessionId;

        var changed = await SendAsync(client, HttpMethod.Put, $"/sessions/{sessionId}/nearby", owner, new { radiusMeters = 250 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var mine = Assert.Single((await ListAsync(client, owner)).EnumerateArray());
        Assert.Equal(250, mine.GetProperty("nearbyRadiusMeters").GetInt32());
    }

    [Fact]
    public async Task Owner_OfASessionNotShownNearby_HasNearbyFalse()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        await AuthTestHelpers.CreateSessionAsync(client, owner);

        var mine = Assert.Single((await ListAsync(client, owner)).EnumerateArray());

        Assert.True(mine.GetProperty("isOwner").GetBoolean());
        Assert.False(mine.GetProperty("nearby").GetBoolean());
        Assert.False(mine.TryGetProperty("nearbyRadiusMeters", out _));
    }

    [Fact]
    public async Task Joiner_IsNotOwner_AndIsNotToldAboutNearby()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        var joiner = await AuthTestHelpers.RegisterAsync(client, "bob", "BO");
        var session = await AuthTestHelpers.CreateSessionAsync(client, owner);

        var join = await SendAsync(client, HttpMethod.Post, $"/session-invites/{session.InviteCode}/join", joiner, new { role = "runner" });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        using (var json = JsonDocument.Parse(await join.Content.ReadAsStringAsync()))
            Assert.False(json.RootElement.GetProperty("isOwner").GetBoolean());

        var theirs = Assert.Single((await ListAsync(client, joiner)).EnumerateArray());
        Assert.False(theirs.GetProperty("isOwner").GetBoolean());
        Assert.False(theirs.TryGetProperty("nearby", out _));
        Assert.False(theirs.TryGetProperty("nearbyRadiusMeters", out _));
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string token)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/sessions", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
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
