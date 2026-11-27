using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotWatcher.Server.Tests;

// Groups that are over (see .ai/plans/go-and-groups.md): GET /me/sessions leaves out ended and expired ones,
// and the hourly maintenance deletes them once they've been over for Groups:RetentionDays.
public class FinishedGroupsTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [Fact]
    public async Task MySessions_LeavesOutAGroupPastItsEndTime()
    {
        var clock = new AdjustableTimeProvider();
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events", "groups", "create-groups"] };
        using var timed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = timed.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        await AuthTestHelpers.CreateSessionAsync(client, kiri, maxLengthHours: 2);

        clock.Advance(TimeSpan.FromMinutes(110));
        Assert.Single(await MySessionsAsync(client, kiri));

        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Empty(await MySessionsAsync(client, kiri));
    }

    [Fact]
    public async Task MySessions_LeavesOutAnEndedSession_ForSomeoneStillInIt()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var bob = await AuthTestHelpers.RegisterAsync(client, "bob", "Bob");
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice);
        await AuthTestHelpers.JoinSessionAsync(client, bob, session.InviteCode, role: "viewer");
        Assert.Single(await MySessionsAsync(client, bob));

        // The last runner leaving archives the session; the viewer's membership row stays.
        var leave = await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{session.SessionId}/membership", alice);
        Assert.True(leave.IsSuccessStatusCode);

        Assert.Empty(await MySessionsAsync(client, bob));
    }

    [Fact]
    public async Task Purge_DeletesAGroup_OnlyOnceItHasBeenOverForTheRetention()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events", "groups", "create-groups"] };
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var group = await AuthTestHelpers.CreateSessionAsync(client, kiri, maxLengthHours: 2);
        var store = factory.Services.GetRequiredService<SessionStore>();
        var endsAt = DateTimeOffset.Parse(group.EndsAt!);

        Assert.Equal(0, store.PurgeFinishedGroups(endsAt + Retention - TimeSpan.FromMinutes(1), Retention));
        Assert.NotNull(store.GetAdminGroup(group.SessionId, DateTimeOffset.UtcNow));

        Assert.Equal(1, store.PurgeFinishedGroups(endsAt + Retention + TimeSpan.FromMinutes(1), Retention));
        Assert.Null(store.GetAdminGroup(group.SessionId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Purge_CountsAnEndedGroupFromWhenItWasEnded()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events", "groups", "create-groups"] };
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var group = await AuthTestHelpers.CreateSessionAsync(client, kiri, maxLengthHours: 240);
        var store = factory.Services.GetRequiredService<SessionStore>();
        Assert.True(store.AdminEndGroup(group.SessionId));

        // Its end time is ten days off, but it was ended now, so a week from now it goes.
        Assert.Equal(1, store.PurgeFinishedGroups(DateTimeOffset.UtcNow + Retention + TimeSpan.FromMinutes(1), Retention));
    }

    [Fact]
    public async Task Purge_KeepsASessionWithRecordedPositions()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, alice, maxLengthHours: 2);
        var posted = await SendAsync(client, HttpMethod.Post, "/location", alice,
            LocationsApiTests.TestLocation("Alice", session.SessionId));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var store = factory.Services.GetRequiredService<SessionStore>();

        Assert.Equal(0, store.PurgeFinishedGroups(DateTimeOffset.UtcNow + TimeSpan.FromDays(30), Retention));
        Assert.NotNull(store.GetAdminGroup(session.SessionId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Purge_LeavesAnActiveGroupAlone()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events", "groups", "create-groups"] };
        using var client = factory.CreateClient();
        var kiri = await AuthTestHelpers.RegisterAsync(client, "kiri", "Kiri");
        var group = await AuthTestHelpers.CreateSessionAsync(client, kiri);
        var store = factory.Services.GetRequiredService<SessionStore>();

        Assert.Equal(0, store.PurgeFinishedGroups(DateTimeOffset.UtcNow, TimeSpan.Zero));
        Assert.Single(await MySessionsAsync(client, kiri));
        Assert.NotNull(store.GetAdminGroup(group.SessionId, DateTimeOffset.UtcNow));
    }

    private static async Task<List<SessionMembership>> MySessionsAsync(HttpClient client, string token) =>
        await (await SendAsync(client, HttpMethod.Get, "/me/sessions", token))
            .Content.ReadFromJsonAsync<List<SessionMembership>>() ?? [];

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
