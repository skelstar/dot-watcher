using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Per-account feature flags, gating crew. See .ai/plans/crew-feature-flag.md.
public class FeatureFlagTests
{
    private static readonly string[] CrewEndpoints =
    [
        "GET /me/crew",
        "POST /me/crew/codes",
        "GET /me/crew/codes",
        "DELETE /me/crew/codes/ABCDEFGH",
        "GET /me/crew/codes/ABCDEFGH",
        "POST /me/crew/codes/ABCDEFGH/redeem",
        "DELETE /me/crew/someone",
        "GET /me/profile",
        "PUT /me/profile",
    ];

    [Fact]
    public async Task CrewEndpoints_WithoutTheFeature_AreNotFound()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        foreach (var endpoint in CrewEndpoints)
        {
            var (method, path) = endpoint.Split(' ') is [var m, var p] ? (new HttpMethod(m), p) : throw new InvalidOperationException();
            using var request = AuthTestHelpers.WithUserToken(method, path, alice.AccessToken);
            if (method == HttpMethod.Put)
                request.Content = JsonContent.Create(new { nickname = "Al" });

            var response = await client.SendAsync(request);

            Assert.True(HttpStatusCode.NotFound == response.StatusCode, $"{endpoint} returned {response.StatusCode}");
        }
    }

    [Fact]
    public async Task CrewEndpoints_WithoutSignIn_StayUnauthorized()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/me/crew");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GrantingTheFeature_OpensCrewForThatAccountOnly()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var bob = await AuthTestHelpers.RegisterWithResponseAsync(client, "bob", "BO");

        var grant = await SetFeaturesAsync(client, alice.User.UserId, new[] { "crew" });

        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/me/crew", alice.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, "/me/crew", bob.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task RevokingTheFeature_ClosesCrewAgain()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await SetFeaturesAsync(client, alice.User.UserId, new[] { "crew" });

        await SetFeaturesAsync(client, alice.User.UserId, Array.Empty<string>());

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, "/me/crew", alice.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task EnabledForAll_OpensCrewForEveryAccount()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["crew"] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/me/crew", alice.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task SetFeatures_NormalisesNames()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        var response = await SetFeaturesAsync(client, alice.User.UserId, new[] { " Crew ", "crew", "", "Beta" });

        var features = (await response.Content.ReadFromJsonAsync<string[]>())!;
        Assert.Equal(new[] { "beta", "crew" }, features);
    }

    [Fact]
    public async Task SetFeatures_RequiresAdminTokenAndAnExistingAccount()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        using var anonymous = new HttpRequestMessage(HttpMethod.Put, $"/admin/users/{alice.User.UserId}/features")
        {
            Content = JsonContent.Create(new { features = new[] { "crew" } }),
        };
        var asUser = AuthTestHelpers.WithUserToken(HttpMethod.Put, $"/admin/users/{alice.User.UserId}/features", alice.AccessToken);
        asUser.Content = JsonContent.Create(new { features = new[] { "crew" } });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(asUser)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetFeaturesAsync(client, "no-such-user", new[] { "crew" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetFeaturesAsync(client, alice.User.UserId, null)).StatusCode);
    }

    [Fact]
    public async Task LoginRegisterAndMe_ReportTheAccountsFeatures()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        Assert.Empty(alice.User.Features!);
        await SetFeaturesAsync(client, alice.User.UserId, new[] { "crew" });

        var login = await client.PostAsJsonAsync("/auth/login", new { username = "alice", password = "correct-horse-password" });
        var loggedIn = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        var me = (await (await GetAsync(client, "/me", alice.AccessToken)).Content.ReadFromJsonAsync<AuthenticatedUser>())!;

        Assert.Equal(new[] { "crew" }, loggedIn.User.Features);
        Assert.Equal(new[] { "crew" }, me.Features);
        Assert.Equal(alice.User.UserId, me.UserId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task AdminUserList_ShowsEachAccountsFeatures()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        await SetFeaturesAsync(client, alice.User.UserId, new[] { "crew" });

        var response = await GetAsync(client, "/admin/users", "test-token");

        var users = (await response.Content.ReadFromJsonAsync<List<AdminUserSummary>>())!;
        Assert.Equal(new[] { "crew" }, Assert.Single(users).Features);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SetFeaturesAsync(HttpClient client, string userId, string[]? features)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/admin/users/{userId}/features")
        {
            Content = JsonContent.Create(new { features }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return await client.SendAsync(request);
    }
}
