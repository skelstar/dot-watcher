using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotWatcher.Server.Tests;

// One active session per organiser, and ending a session. See .ai/plans/nearby-join.md (task N3b).
public class NearbyActiveSessionTests
{
    [Fact]
    public async Task Create_WhileASessionIsActive_IsRefused_WithTheExistingSessionId()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var first = await CreateAsync(client, token, "FIRST01");

        var second = await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = "SECOND2" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(first.SessionId, json.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task Create_WithoutTheEventsFeature_StillAllowsSeveralSessions()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await CreateAsync(client, token, "FIRST01");

        var second = await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = "SECOND2" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Create_AfterTheSessionIsEnded_IsAllowed()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var first = await CreateAsync(client, token, "FIRST01");
        await SendAsync(client, HttpMethod.Post, $"/sessions/{first.SessionId}/end", token);

        var second = await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = "SECOND2" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Create_AfterTheEventLengthHasPassed_IsAllowed()
    {
        var clock = new AdjustableTimeProvider();
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var timed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = timed.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = "FIRST01", maxLengthHours = 2 });

        clock.Advance(TimeSpan.FromMinutes(90));
        var stillActive = await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = "SECOND2" });
        clock.Advance(TimeSpan.FromMinutes(90));
        var afterLength = await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = "SECOND2" });

        Assert.Equal(HttpStatusCode.Conflict, stillActive.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterLength.StatusCode);
    }

    [Fact]
    public async Task End_ArchivesTheSession_RemovesMembers_AndStopsJoining()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var member = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var late = await AuthTestHelpers.RegisterAsync(client, "carol", "Carol");
        var session = await CreateAsync(client, owner, "FIRST01");
        await SendAsync(client, HttpMethod.Post, $"/session-invites/{session.InviteCode}/join", member, new { role = "runner" });

        var end = await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", owner);
        var rejoin = await SendAsync(client, HttpMethod.Post, $"/session-invites/{session.InviteCode}/join", late, new { role = "runner" });

        Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, rejoin.StatusCode);
        foreach (var token in new[] { owner, member })
        {
            var mine = await SendAsync(client, HttpMethod.Get, "/me/sessions", token);
            Assert.DoesNotContain(await mine.Content.ReadFromJsonAsync<List<SessionMembership>>() ?? [],
                s => s.SessionId == session.SessionId);
        }
    }

    [Fact]
    public async Task End_StopsTheSessionAppearingNearby()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var other = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var created = await SendAsync(client, HttpMethod.Post, "/sessions", owner, new
        {
            sessionName = "Sunday long run", nearby = true, latitude = -36.85, longitude = 174.76, accuracy = 8,
        });
        var session = (await created.Content.ReadFromJsonAsync<SessionMembership>())!;

        await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", owner);
        var list = await SendAsync(client, HttpMethod.Get, "/me/events/nearby?lat=-36.85&lon=174.76&accuracy=5", other);

        Assert.Empty(await list.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task End_IsOwnerOnly_AndLooksTheSameForUnknownIds()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var member = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var session = await CreateAsync(client, owner, "FIRST01");
        await SendAsync(client, HttpMethod.Post, $"/session-invites/{session.InviteCode}/join", member, new { role = "runner" });

        var byMember = await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", member);
        var unknown = await SendAsync(client, HttpMethod.Post, "/sessions/no-such-session/end", owner);

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        var stillThere = await SendAsync(client, HttpMethod.Get, "/me/sessions", owner);
        Assert.Contains(await stillThere.Content.ReadFromJsonAsync<List<SessionMembership>>() ?? [],
            s => s.SessionId == session.SessionId);
    }

    [Fact]
    public async Task End_TwiceSucceeds()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateAsync(client, owner, "FIRST01");

        await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", owner);
        var again = await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", owner);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task End_WithoutTheEventsFeature_IsNotFound()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var owner = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await CreateAsync(client, owner, "FIRST01");

        var end = await SendAsync(client, HttpMethod.Post, $"/sessions/{session.SessionId}/end", owner);

        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
    }

    [Fact]
    public async Task End_WithoutSignIn_IsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/sessions/any/end", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<SessionMembership> CreateAsync(HttpClient client, string token, string name)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/sessions", token, new { sessionName = name });
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

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
