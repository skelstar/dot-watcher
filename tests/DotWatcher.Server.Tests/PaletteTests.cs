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
    public void Contrast_MatchesTheWcagReferenceValues()
    {
        Assert.Equal(21.0, PaletteProvider.ContrastRatio("#000000", "#ffffff"), 2);
        Assert.Equal(1.0, PaletteProvider.ContrastRatio("#336699", "#336699"), 2);
    }

    private static async Task<AuthenticatedUser> GetMeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<AuthenticatedUser>())!;
    }
}
