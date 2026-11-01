using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Per-account feature grants: stored and reported, no longer gating crew, casual or events.
public class FeatureFlagTests
{
    [Fact]
    public async Task CrewEndpoints_WithoutSignIn_StayUnauthorized()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/me/crew");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        // Other accounts exist too (the server seeds dw-demo at startup), so pick alice out.
        Assert.Equal(new[] { "crew" }, users.Single(u => u.Id == alice.User.UserId).Features);
        Assert.All(users.Where(u => u.Id != alice.User.UserId), u => Assert.Empty(u.Features));
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
