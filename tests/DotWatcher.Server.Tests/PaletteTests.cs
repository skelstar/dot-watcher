using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// The remote palette: served from the Palette config on login, register and GET /me; a bad value serves none.
public class PaletteTests
{
    private static Dictionary<string, string> Good() => new()
    {
        ["Version"] = "1",
        ["GoBg"] = "#6f1a07", ["GoFg"] = "#f7f3e3", ["GoBar"] = "#af9164",
        ["AltBg"] = "#f7f3e3", ["AltFg"] = "#2b2118",
        ["Accent"] = "#af9164", ["AccentInk"] = "#2b2118",
        ["Screen"] = "#ffffff",
    };

    [Fact]
    public async Task RegisterLoginAndMe_IncludeThePaletteWhenConfigured()
    {
        using var factory = new DotWatcherApiFactory { Palette = Good() };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        var login = await client.PostAsJsonAsync("/auth/login", new { username = "alice", password = "correct-horse-password" });
        var loggedIn = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        var me = await GetMeAsync(client, alice.AccessToken);

        foreach (var palette in new[] { alice.User.Config?.Palette, loggedIn.User.Config?.Palette, me.Config?.Palette })
        {
            Assert.NotNull(palette);
            Assert.Equal(1, palette.Version);
            Assert.Equal("#6f1a07", palette.GoBg);
            Assert.Equal("#ffffff", palette.Screen);
        }
    }

    [Fact]
    public async Task Me_OmitsThePaletteWhenNotConfigured()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        var me = await GetMeAsync(client, alice.AccessToken);

        Assert.Null(alice.User.Config?.Palette);
        Assert.Null(me.Config?.Palette);
    }

    [Theory]
    [InlineData("GoBg", "6f1a07")]       // no leading #
    [InlineData("Screen", "#fff")]       // short form
    [InlineData("Accent", "#zzzzzz")]    // not hex
    [InlineData("GoFg", "#7f2a17")]      // too close to GoBg
    [InlineData("Version", "2")]         // a version this server doesn't know
    public async Task AnInvalidPalette_IsNotServed_AndDoesNotFailTheResponse(string key, string value)
    {
        var palette = Good();
        palette[key] = value;
        using var factory = new DotWatcherApiFactory { Palette = palette };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        var me = await GetMeAsync(client, alice.AccessToken);

        Assert.Null(alice.User.Config?.Palette);
        Assert.Null(me.Config?.Palette);
    }

    [Fact]
    public async Task PostAdminPalette_IsServedInPlaceOfTheConfigPalette_UntilCleared()
    {
        using var factory = new DotWatcherApiFactory { Palette = Good() };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var posted = Good();
        posted["GoBg"] = "#112233";
        posted["Label"] = " 1.2.2 ";
        posted["Name"] = " Navy and orange ";

        var set = await SendAdminAsync(client, HttpMethod.Post, "test-token", posted);
        var served = (await GetMeAsync(client, alice.AccessToken)).Config?.Palette;
        var cleared = await SendAdminAsync(client, HttpMethod.Delete, "test-token");
        var after = (await GetMeAsync(client, alice.AccessToken)).Config?.Palette;

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("#112233", served?.GoBg);
        Assert.Equal("1.2.2", served?.Label);
        Assert.Equal("Navy and orange", served?.Name);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal("#6f1a07", after?.GoBg);
        Assert.Null(after?.Label);
        Assert.Null(after?.Name);
    }

    [Fact]
    public async Task PostAdminPalette_RejectsAnInvalidPalette_AndABadToken()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var poor = Good();
        poor["GoFg"] = "#7f2a17"; // too close to GoBg
        var longLabel = Good();
        longLabel["Label"] = new string('x', 33);
        var longName = Good();
        longName["Name"] = new string('x', 31);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAdminAsync(client, HttpMethod.Post, "test-token", poor)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAdminAsync(client, HttpMethod.Post, "test-token", longLabel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAdminAsync(client, HttpMethod.Post, "test-token", longName)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAdminAsync(client, HttpMethod.Post, "wrong", Good())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAdminAsync(client, HttpMethod.Delete, "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAdminAsync(client, HttpMethod.Delete, "test-token")).StatusCode);
    }

    [Fact]
    public void Contrast_MatchesTheWcagReferenceValues()
    {
        Assert.Equal(21.0, PaletteProvider.ContrastRatio("#000000", "#ffffff"), 2);
        Assert.Equal(1.0, PaletteProvider.ContrastRatio("#336699", "#336699"), 2);
    }

    private static Task<HttpResponseMessage> SendAdminAsync(HttpClient client, HttpMethod method, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, "/admin/palette");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            // Body keys are the config keys; the API is camelCase JSON.
            var json = body is Dictionary<string, string> d
                ? d.ToDictionary(kv => char.ToLowerInvariant(kv.Key[0]) + kv.Key[1..], kv => kv.Key == "Version" ? (object)int.Parse(kv.Value) : kv.Value)
                : body;
            request.Content = JsonContent.Create(json);
        }
        return client.SendAsync(request);
    }

    private static async Task<AuthenticatedUser> GetMeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<AuthenticatedUser>())!;
    }
}
