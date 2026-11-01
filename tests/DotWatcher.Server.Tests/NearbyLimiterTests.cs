using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Per-account caps on the nearby list and join. See .ai/plans/nearby-join.md (task N6).
public class NearbyLimiterTests
{
    [Fact]
    public void Joins_AreCappedPerUser_AndReopenAfterAnHour()
    {
        var clock = new ManualTimeProvider();
        var limiter = new NearbyLimiter(clock);

        for (var i = 0; i < NearbyLimiter.JoinsPerUserPerHour; i++)
            Assert.True(limiter.TryAcceptJoin("alice"));

        Assert.False(limiter.TryAcceptJoin("alice"));
        Assert.True(limiter.TryAcceptJoin("bob"));

        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(limiter.TryAcceptJoin("alice"));
    }

    [Fact]
    public void Lists_AndJoins_HaveSeparateBudgets()
    {
        var limiter = new NearbyLimiter(new ManualTimeProvider());

        for (var i = 0; i < NearbyLimiter.JoinsPerUserPerHour; i++)
            limiter.TryAcceptJoin("alice");

        Assert.False(limiter.TryAcceptJoin("alice"));
        Assert.True(limiter.TryAcceptList("alice"));
    }

    [Fact]
    public void Lists_AreCappedPerUser()
    {
        var limiter = new NearbyLimiter(new ManualTimeProvider());

        for (var i = 0; i < NearbyLimiter.ListsPerUserPerHour; i++)
            Assert.True(limiter.TryAcceptList("alice"));

        Assert.False(limiter.TryAcceptList("alice"));
    }

    [Fact]
    public async Task JoinEndpoint_AnswersTooManyRequests_OnceTheBudgetIsSpent()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        for (var i = 0; i < NearbyLimiter.JoinsPerUserPerHour; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(client, token)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await JoinAsync(client, token)).StatusCode);
    }

    [Fact]
    public async Task ListEndpoint_AnswersTooManyRequests_ButNeverLimitsThePlaceholderCall()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        for (var i = 0; i < NearbyLimiter.ListsPerUserPerHour; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, token, "?lat=-36.85&lon=174.76")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetAsync(client, token, "?lat=-36.85&lon=174.76")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, token, "")).StatusCode);
    }

    private static async Task<HttpResponseMessage> JoinAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/nearby/sessions/no-such-session/join")
        {
            Content = JsonContent.Create(new { latitude = -36.85, longitude = 174.76 }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/me/events/nearby{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
