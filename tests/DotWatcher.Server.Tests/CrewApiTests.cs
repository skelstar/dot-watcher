using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DotWatcher.Server.Tests;

// Crew: a mutual, per-account list built only by redeeming a one-time code the other person sent.
// See .ai/plans/crew-management.md.
public class CrewApiTests
{
    [Fact]
    public async Task CreateCode_ReturnsEightCharacterCodeExpiringInAboutADay()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var before = DateTimeOffset.UtcNow;
        var code = await CreateCodeAsync(client, alice.AccessToken);

        Assert.Equal(8, code.Code.Length);
        Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{8}$", code.Code);
        var expiresAt = DateTimeOffset.Parse(code.ExpiresAt);
        Assert.InRange(expiresAt - before, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
    }

    [Fact]
    public async Task Redeem_MakesBothPeopleCrew()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var code = await CreateCodeAsync(client, alice.AccessToken);

        var preview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{code.Code}", bob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var owner = (await preview.Content.ReadFromJsonAsync<CrewCodePreview>())!;
        Assert.Equal(alice.User.UserId, owner.UserId);
        Assert.Equal("AL", owner.DisplayName);
        Assert.False(owner.AlreadyCrew);
        // Previewing adds no one.
        Assert.Empty((await GetCrewAsync(client, bob.AccessToken)).Crew);

        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        var added = (await redeem.Content.ReadFromJsonAsync<CrewMember>())!;
        Assert.Equal(alice.User.UserId, added.UserId);

        var bobCrew = (await GetCrewAsync(client, bob.AccessToken)).Crew;
        var aliceCrew = (await GetCrewAsync(client, alice.AccessToken)).Crew;
        Assert.Equal(alice.User.UserId, Assert.Single(bobCrew).UserId);
        Assert.Equal(bob.User.UserId, Assert.Single(aliceCrew).UserId);
    }

    [Fact]
    public async Task Redeem_IsSingleUse_SecondPersonIsRefused()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var carol = await RegisterAsync(client, "carol", "CA");
        var code = await CreateCodeAsync(client, alice.AccessToken);

        var first = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);
        var second = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", carol.AccessToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Empty((await GetCrewAsync(client, carol.AccessToken)).Crew);
        Assert.Equal(bob.User.UserId, Assert.Single((await GetCrewAsync(client, alice.AccessToken)).Crew).UserId);
    }

    [Fact]
    public async Task Redeem_WhenSeveralPeopleRedeemAtOnce_ExactlyOneWins()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var code = await CreateCodeAsync(client, alice.AccessToken);

        var rivals = new List<AuthResponse>();
        for (var i = 0; i < 6; i++)
            rivals.Add(await RegisterAsync(client, $"rival{i}", $"R{i}"));

        var responses = await Task.WhenAll(rivals.Select(rival =>
            SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", rival.AccessToken)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.NotFound));
        Assert.Single((await GetCrewAsync(client, alice.AccessToken)).Crew);
    }

    [Fact]
    public async Task Redeem_AfterTwentyFourHours_IsRefused()
    {
        var clock = new AdjustableTimeProvider();
        using var factory = new DotWatcherApiFactory();
        using var timed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = timed.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var code = await CreateCodeAsync(client, alice.AccessToken);

        clock.Advance(TimeSpan.FromHours(23));
        var stillGood = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{code.Code}", bob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, stillGood.StatusCode);

        clock.Advance(TimeSpan.FromHours(2));
        var preview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{code.Code}", bob.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
        Assert.Empty((await GetCrewAsync(client, bob.AccessToken)).Crew);
        Assert.Empty(await GetCodesAsync(client, alice.AccessToken));
    }

    [Fact]
    public async Task Redeem_OwnCode_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var code = await CreateCodeAsync(client, alice.AccessToken);

        var preview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{code.Code}", alice.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", alice.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        // Trying your own code doesn't use it up.
        Assert.Single(await GetCodesAsync(client, alice.AccessToken));
    }

    [Theory]
    [InlineData("ZZZZZZZZ")] // well-formed but never issued
    [InlineData("abc")]      // too short
    [InlineData("AAAA0000")] // contains characters that are never issued (0)
    public async Task Redeem_UnknownOrMalformedCode_ReturnsTheSameNotFound(string code)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var bob = await RegisterAsync(client, "bob", "BO");

        var preview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{code}", bob.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code}/redeem", bob.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
        Assert.Equal(await preview.Content.ReadAsStringAsync(), await redeem.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Redeem_WhenAlreadyCrew_SucceedsWithoutUsingTheCodeUp()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var carol = await RegisterAsync(client, "carol", "CA");

        var first = await CreateCodeAsync(client, alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{first.Code}/redeem", bob.AccessToken);
        var second = await CreateCodeAsync(client, alice.AccessToken);

        var preview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{second.Code}", bob.AccessToken);
        Assert.True((await preview.Content.ReadFromJsonAsync<CrewCodePreview>())!.AlreadyCrew);

        var again = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{second.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // The code wasn't consumed, so someone else can still use it.
        var other = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{second.Code}/redeem", carol.AccessToken);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(2, (await GetCrewAsync(client, alice.AccessToken)).Crew.Count);
        Assert.Single((await GetCrewAsync(client, bob.AccessToken)).Crew);
    }

    [Fact]
    public async Task CreateCode_WithThreeLive_RetiresTheOldest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");

        var oldest = await CreateCodeAsync(client, alice.AccessToken);
        var second = await CreateCodeAsync(client, alice.AccessToken);
        var third = await CreateCodeAsync(client, alice.AccessToken);
        var fourth = await CreateCodeAsync(client, alice.AccessToken);

        var live = (await GetCodesAsync(client, alice.AccessToken)).Select(c => c.Code).ToList();
        Assert.Equal(3, live.Count);
        Assert.DoesNotContain(oldest.Code, live);
        Assert.Equal(new[] { fourth.Code, third.Code, second.Code }, live);

        var redeemOldest = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{oldest.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, redeemOldest.StatusCode);
    }

    [Fact]
    public async Task RevokeCode_StopsItWorking_AndOnlyTheOwnerCanRevoke()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");
        var code = await CreateCodeAsync(client, alice.AccessToken);

        var byStranger = await SendAsync(client, HttpMethod.Delete, $"/me/crew/codes/{code.Code}", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, byStranger.StatusCode);

        var byOwner = await SendAsync(client, HttpMethod.Delete, $"/me/crew/codes/{code.Code}", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);

        var again = await SendAsync(client, HttpMethod.Delete, $"/me/crew/codes/{code.Code}", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
    }

    [Fact]
    public async Task RemoveCrewMember_IsImmediateForBothPeople()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");

        var remove = await SendAsync(client, HttpMethod.Delete, $"/me/crew/{bob.User.UserId}", alice.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty((await GetCrewAsync(client, alice.AccessToken)).Crew);
        Assert.Empty((await GetCrewAsync(client, bob.AccessToken)).Crew);

        var again = await SendAsync(client, HttpMethod.Delete, $"/me/crew/{bob.User.UserId}", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Bob_CanRemoveAlice_AsWellAsAliceRemovingBob()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");

        var remove = await SendAsync(client, HttpMethod.Delete, $"/me/crew/{alice.User.UserId}", bob.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty((await GetCrewAsync(client, alice.AccessToken)).Crew);
    }

    [Fact]
    public async Task Blocking_EndsTheCrewLink_AndBlockedPeopleCannotRedeem()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");

        var block = await SendAsync(client, HttpMethod.Post, $"/me/blocks/{bob.User.UserId}", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);

        Assert.Empty((await GetCrewAsync(client, alice.AccessToken)).Crew);
        Assert.Empty((await GetCrewAsync(client, bob.AccessToken)).Crew);

        var code = await CreateCodeAsync(client, alice.AccessToken);
        var preview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{code.Code}", bob.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);

        // It works the other way round too: the blocker can't use the blocked person's code.
        var bobCode = await CreateCodeAsync(client, bob.AccessToken);
        var aliceRedeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{bobCode.Code}/redeem", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, aliceRedeem.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithCrewLinksAndCodes_Succeeds()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var (alice, bob) = await MakeCrewAsync(client, "alice", "bob");
        var carol = await RegisterAsync(client, "carol", "CA");

        // alice has an unused code; carol has redeemed a code of bob's (so bob is the owner and carol
        // is the redeemer on a crew_codes row).
        await CreateCodeAsync(client, alice.AccessToken);
        var bobCode = await CreateCodeAsync(client, bob.AccessToken);
        await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{bobCode.Code}/redeem", carol.AccessToken);

        // The redeemer, a crew member, and finally the owner of an unused code all delete cleanly.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, "/me", carol.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, "/me", bob.AccessToken)).StatusCode);
        Assert.Empty((await GetCrewAsync(client, alice.AccessToken)).Crew);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, "/me", alice.AccessToken)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/me/crew")]
    [InlineData("DELETE", "/me/crew/someone")]
    [InlineData("POST", "/me/crew/codes")]
    [InlineData("GET", "/me/crew/codes")]
    [InlineData("DELETE", "/me/crew/codes/ABCDEFGH")]
    [InlineData("GET", "/me/crew/codes/ABCDEFGH")]
    [InlineData("POST", "/me/crew/codes/ABCDEFGH/redeem")]
    [InlineData("GET", "/me/profile")]
    [InlineData("PUT", "/me/profile")]
    public async Task Endpoints_RequireAUserToken(string method, string url)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT")
            request.Content = JsonContent.Create(new { nickname = "x" });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CodeAttempts_AreRateLimitedPerUser()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var bob = await RegisterAsync(client, "bob", "BO");

        for (var i = 0; i < 20; i++)
        {
            var attempt = await SendAsync(client, HttpMethod.Get, "/me/crew/codes/ZZZZZZZZ", bob.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
        }

        var preview = await SendAsync(client, HttpMethod.Get, "/me/crew/codes/ZZZZZZZZ", bob.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, "/me/crew/codes/ZZZZZZZZ/redeem", bob.AccessToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, preview.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, redeem.StatusCode);

        // Someone else is unaffected.
        var alice = await RegisterAsync(client, "alice", "AL");
        var other = await SendAsync(client, HttpMethod.Get, "/me/crew/codes/ZZZZZZZZ", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task Nickname_IsUnsetByDefault_CanBeSetTrimmedAndCleared()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        Assert.Null((await GetProfileAsync(client, alice.AccessToken)).Nickname);

        var set = await SendAsync(client, HttpMethod.Put, "/me/profile", alice.AccessToken, new { nickname = "  Priya  " });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("Priya", (await set.Content.ReadFromJsonAsync<ProfileResponse>())!.Nickname);
        Assert.Equal("Priya", (await GetProfileAsync(client, alice.AccessToken)).Nickname);

        var clear = await SendAsync(client, HttpMethod.Put, "/me/profile", alice.AccessToken, new { nickname = "   " });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null((await GetProfileAsync(client, alice.AccessToken)).Nickname);
    }

    [Theory]
    [InlineData("1234567890123456789012345678901")]  // 31 characters
    [InlineData("tab\there")]
    [InlineData("new\nline")]
    public async Task Nickname_TooLongOrWithControlCharacters_IsRejected(string nickname)
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var response = await SendAsync(client, HttpMethod.Put, "/me/profile", alice.AccessToken, new { nickname });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetProfileAsync(client, alice.AccessToken)).Nickname);
    }

    [Fact]
    public async Task Nickname_IsWhatCrewSees_FallingBackToInitials()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        var bob = await RegisterAsync(client, "bob", "BO");

        var withoutNickname = await CreateCodeAsync(client, alice.AccessToken);
        var firstPreview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{withoutNickname.Code}", bob.AccessToken);
        var unnamed = (await firstPreview.Content.ReadFromJsonAsync<CrewCodePreview>())!;
        Assert.Null(unnamed.Nickname);
        Assert.Equal("AL", unnamed.DisplayName);

        await SendAsync(client, HttpMethod.Put, "/me/profile", alice.AccessToken, new { nickname = "Priya" });
        var secondPreview = await SendAsync(client, HttpMethod.Get, $"/me/crew/codes/{withoutNickname.Code}", bob.AccessToken);
        Assert.Equal("Priya", (await secondPreview.Content.ReadFromJsonAsync<CrewCodePreview>())!.Nickname);

        await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{withoutNickname.Code}/redeem", bob.AccessToken);
        var member = Assert.Single((await GetCrewAsync(client, bob.AccessToken)).Crew);
        Assert.Equal("Priya", member.Nickname);
        Assert.Equal("AL", member.DisplayName);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username, string displayName) =>
        await AuthTestHelpers.RegisterWithResponseAsync(client, username, displayName);

    private static async Task<(AuthResponse Alice, AuthResponse Bob)> MakeCrewAsync(
        HttpClient client, string ownerUsername, string joinerUsername)
    {
        var owner = await RegisterAsync(client, ownerUsername, ownerUsername[..2].ToUpperInvariant());
        var joiner = await RegisterAsync(client, joinerUsername, joinerUsername[..2].ToUpperInvariant());
        var code = await CreateCodeAsync(client, owner.AccessToken);
        var redeem = await SendAsync(client, HttpMethod.Post, $"/me/crew/codes/{code.Code}/redeem", joiner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        return (owner, joiner);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    internal static async Task<CrewCode> CreateCodeAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/me/crew/codes", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrewCode>())!;
    }

    private static async Task<IReadOnlyList<CrewCode>> GetCodesAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/crew/codes", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<CrewCode>>())!;
    }

    private static async Task<CrewOverview> GetCrewAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/crew", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CrewOverview>())!;
    }

    private static async Task<ProfileResponse> GetProfileAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/profile", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
