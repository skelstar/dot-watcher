using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace DotWatcher.Server.Tests;

// Watch links: a secret link to one casual run that anyone can open without an account. See
// .ai/plans/casual-tracking.md.
public class CasualWatchTests
{
    private const double Latitude = -41.17;
    private const double Longitude = 174.7762;

    private static DotWatcherApiFactory CasualFactory() =>
        new() { EnabledForAll = [Features.Crew, Features.Casual] };

    [Fact]
    public async Task CreateLink_WithoutTheFeature_LooksAbsent()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Crew] };
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var response = await SendAsync(client, HttpMethod.Post, "/casual/watch-link", alice.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateLink_WhenNotOut_IsAConflict()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");

        var response = await SendAsync(client, HttpMethod.Post, "/casual/watch-link", alice.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Link_ShowsTheRunnersPosition_WithNoSignIn()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        // No Authorization header at all.
        var response = await client.GetAsync(link.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = (await response.Content.ReadFromJsonAsync<CasualWatchView>())!;
        Assert.Equal("AL", view.Label);
        Assert.False(view.Ended);
        Assert.True(view.Live);
        Assert.Equal(Latitude, view.Position!.Latitude);
        Assert.Equal(Longitude, view.Position.Longitude);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Link_ShowsOnlyOneRunner_AndNothingElse()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        var json = await client.GetStringAsync(link.Path);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            new[] { "ended", "label", "live", "position" },
            document.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain(alice.User.UserId, json);
        Assert.DoesNotContain("alice", json);
    }

    [Fact]
    public async Task Link_BeforeTheFirstPosition_HasNoPositionYet()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await SendAsync(client, HttpMethod.Post, "/casual/start", alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        var view = (await client.GetFromJsonAsync<CasualWatchView>(link.Path))!;

        Assert.False(view.Ended);
        Assert.False(view.Live);
        Assert.Null(view.Position);
    }

    [Fact]
    public async Task Link_AfterStop_SaysTheRunEnded_AndGivesNoPosition()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);
        var view = (await client.GetFromJsonAsync<CasualWatchView>(link.Path))!;

        Assert.True(view.Ended);
        Assert.False(view.Live);
        Assert.Null(view.Position);
    }

    [Fact]
    public async Task ANewRun_DoesNotReviveAnOldLink()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var oldLink = await CreateLinkAsync(client, alice.AccessToken);
        await SendAsync(client, HttpMethod.Post, "/casual/stop", alice.AccessToken);

        await StartAndPostAsync(client, alice.AccessToken);

        var view = (await client.GetFromJsonAsync<CasualWatchView>(oldLink.Path))!;
        Assert.True(view.Ended);
        Assert.Null(view.Position);
    }

    [Fact]
    public async Task EachCall_MakesANewLink_AndAllWorkUntilRevoked()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var first = await CreateLinkAsync(client, alice.AccessToken);
        var second = await CreateLinkAsync(client, alice.AccessToken);

        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(first.Path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(second.Path)).StatusCode);

        var revoke = await SendAsync(client, HttpMethod.Delete, "/casual/watch-link", alice.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(first.Path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(second.Path)).StatusCode);
    }

    [Fact]
    public async Task Token_IsLongAndRandom()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);

        var tokens = new HashSet<string>();
        for (var i = 0; i < 5; i++)
            tokens.Add((await CreateLinkAsync(client, alice.AccessToken)).Token);

        Assert.Equal(5, tokens.Count);
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]{22}$", t));
    }

    [Fact]
    public async Task OnlyAHashOfTheSecret_IsStored()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        using var conn = new NpgsqlConnection($"Host=localhost;Port=5432;Database=dotwatcher;Username=dotwatcher;Password=dotwatcher-dev;SearchPath={factory.Schema}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT token_hash FROM casual_watch_links";
        var stored = (string)cmd.ExecuteScalar()!;

        Assert.NotEqual(link.Token, stored);
        Assert.DoesNotContain(link.Token, stored);
        Assert.Equal(64, stored.Length);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AnUnknownOrMalformedLink_IsNotFound(string token)
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/watch/{token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ManyWrongGuesses_AreLimited_ButAValidLinkStillWorks()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        HttpStatusCode last = default;
        for (var i = 0; i < 125; i++)
            last = (await client.GetAsync("/watch/AAAAAAAAAAAAAAAAAAAAAA")).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(link.Path)).StatusCode);
    }

    [Fact]
    public async Task DeletingTheAccount_RemovesItsLinks()
    {
        using var factory = CasualFactory();
        using var client = factory.CreateClient();
        var alice = await RegisterAsync(client, "alice", "AL");
        await StartAndPostAsync(client, alice.AccessToken);
        var link = await CreateLinkAsync(client, alice.AccessToken);

        var delete = await SendAsync(client, HttpMethod.Delete, "/me", alice.AccessToken);
        Assert.True(delete.IsSuccessStatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(link.Path)).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username, string displayName) =>
        await AuthTestHelpers.RegisterWithResponseAsync(client, username, displayName);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string accessToken, object? body = null)
    {
        using var request = AuthTestHelpers.WithUserToken(method, url, accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task StartAndPostAsync(HttpClient client, string accessToken)
    {
        var start = await SendAsync(client, HttpMethod.Post, "/casual/start", accessToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var post = await LocationsApiTests.PostLocationAsync(
            client,
            new LocationUpdate("ignored", SessionStore.CasualSessionId, Latitude, Longitude, 90, DateTimeOffset.UtcNow),
            accessToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    private static async Task<CasualWatchLinkResponse> CreateLinkAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/casual/watch-link", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<CasualWatchLinkResponse>())!;
        Assert.Equal($"/watch/{link.Token}", link.Path);
        return link;
    }
}
