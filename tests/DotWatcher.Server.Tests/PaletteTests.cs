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
    public async Task APalette_ReachesNobody_UntilItIsAssignedToTheirAppVersion()
    {
        using var factory = new DotWatcherApiFactory { Palette = Good() };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var created = await CreateAsync(client, "Test", "t-1", goBg: "#112233", goFg: "#ffffff");

        var before = (await GetMeAsync(client, alice.AccessToken, "1.2.2 (20)")).Config?.Palette;
        var assigned = await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/1.2.2", "test-token", new { paletteId = created.Id });
        var onVersion = (await GetMeAsync(client, alice.AccessToken, "1.2.2 (20)")).Config?.Palette;

        Assert.Equal("#6f1a07", before?.GoBg); // config palette, until assigned
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal("#112233", onVersion?.GoBg);
        Assert.Equal("t-1", onVersion?.Label);
    }

    [Fact]
    public async Task AnUnassignedVersion_GetsTheHighestAssignedVersionsPalette_NotTheFallback()
    {
        using var factory = new DotWatcherApiFactory { Palette = Good() };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var fallback = await CreateAsync(client, "Fallback", goBg: "#112233", goFg: "#ffffff");
        var older = await CreateAsync(client, "Older", goBg: "#223344", goFg: "#ffffff");
        var newest = await CreateAsync(client, "Newest", goBg: "#334455", goFg: "#ffffff");
        await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/fallback", "test-token", new { paletteId = fallback.Id });
        await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/1.9.2", "test-token", new { paletteId = older.Id });
        await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/1.10.0", "test-token", new { paletteId = newest.Id });

        Assert.Equal("#223344", (await GetMeAsync(client, alice.AccessToken, "1.9.2 (20)")).Config?.Palette?.GoBg); // exact match
        Assert.Equal("#334455", (await GetMeAsync(client, alice.AccessToken, "1.10.0 (30)")).Config?.Palette?.GoBg);
        Assert.Equal("#334455", (await GetMeAsync(client, alice.AccessToken, "1.9.5 (25)")).Config?.Palette?.GoBg); // numeric, not text, order
        Assert.Equal("#334455", (await GetMeAsync(client, alice.AccessToken, "9.9.9 (1)")).Config?.Palette?.GoBg);
        Assert.Equal("#334455", (await GetMeAsync(client, alice.AccessToken)).Config?.Palette?.GoBg);
    }

    [Fact]
    public async Task TheFallbackAssignment_IsUsedOnlyWhileNoVersionIsAssigned_AndUnassignFallsBack()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var fallback = await CreateAsync(client, "Fallback", goBg: "#112233", goFg: "#ffffff");
        var special = await CreateAsync(client, "Special", goBg: "#223344", goFg: "#ffffff");
        await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/fallback", "test-token", new { paletteId = fallback.Id });

        Assert.Equal("#112233", (await GetMeAsync(client, alice.AccessToken, "9.9.9 (1)")).Config?.Palette?.GoBg);
        Assert.Equal("#112233", (await GetMeAsync(client, alice.AccessToken)).Config?.Palette?.GoBg);

        await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/1.2.2", "test-token", new { paletteId = special.Id });
        Assert.Equal("#223344", (await GetMeAsync(client, alice.AccessToken, "1.2.2 (20)")).Config?.Palette?.GoBg);
        Assert.Equal("#223344", (await GetMeAsync(client, alice.AccessToken, "9.9.9 (1)")).Config?.Palette?.GoBg); // latest version, not fallback

        var unassigned = await SendAdminAsync(client, HttpMethod.Delete, "/admin/palettes/assignments/1.2.2", "test-token");
        Assert.Equal(HttpStatusCode.NoContent, unassigned.StatusCode);
        Assert.Equal("#112233", (await GetMeAsync(client, alice.AccessToken, "1.2.2 (20)")).Config?.Palette?.GoBg);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAdminAsync(client, HttpMethod.Delete, "/admin/palettes/assignments/1.2.2", "test-token")).StatusCode);
    }

    [Fact]
    public async Task ThePaletteLibrary_ListsGetsUpdatesAndDeletes_AndRefusesToDeleteOneInUse()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");
        var created = await CreateAsync(client, "Navy", "n-1", goBg: "#112233", goFg: "#ffffff");
        await SendAdminAsync(client, HttpMethod.Put, "/admin/palettes/assignments/1.2.2", "test-token", new { paletteId = created.Id });

        var list = (await (await SendAdminAsync(client, HttpMethod.Get, "/admin/palettes", "test-token")).Content.ReadFromJsonAsync<List<PaletteEntry>>())!;
        var updated = await SendAdminAsync(client, HttpMethod.Put, $"/admin/palettes/{created.Id}", "test-token", Body("Navy v2", "n-2", goBg: "#001122", goFg: "#ffffff"));
        var served = (await GetMeAsync(client, alice.AccessToken, "1.2.2 (20)")).Config?.Palette;
        var refused = await SendAdminAsync(client, HttpMethod.Delete, $"/admin/palettes/{created.Id}", "test-token");
        await SendAdminAsync(client, HttpMethod.Delete, "/admin/palettes/assignments/1.2.2", "test-token");
        var deleted = await SendAdminAsync(client, HttpMethod.Delete, $"/admin/palettes/{created.Id}", "test-token");
        var gone = await SendAdminAsync(client, HttpMethod.Get, $"/admin/palettes/{created.Id}", "test-token");

        var entry = Assert.Single(list);
        Assert.Equal(new[] { "1.2.2" }, entry.AssignedTo);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Navy v2", served?.Name); // editing an assigned palette is live
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task PaletteAdmin_RejectsBadInput_AndABadToken()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var created = await CreateAsync(client, "Ok");

        async Task<HttpStatusCode> Status(HttpMethod m, string path, string token, object? body = null) =>
            (await SendAdminAsync(client, m, path, token, body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Status(HttpMethod.Post, "/admin/palettes", "test-token", Body("Bad", goFg: "red")));      // not #rrggbb
        Assert.Equal(HttpStatusCode.BadRequest, await Status(HttpMethod.Post, "/admin/palettes", "test-token", Body(name: null)));            // name required
        Assert.Equal(HttpStatusCode.BadRequest, await Status(HttpMethod.Post, "/admin/palettes", "test-token", Body(new string('x', 31))));  // name too long
        Assert.Equal(HttpStatusCode.BadRequest, await Status(HttpMethod.Post, "/admin/palettes", "test-token", Body("Ok", new string('x', 33))));
        Assert.Equal(HttpStatusCode.NotFound, await Status(HttpMethod.Put, "/admin/palettes/nope", "test-token", Body("Ok")));
        Assert.Equal(HttpStatusCode.NotFound, await Status(HttpMethod.Put, "/admin/palettes/assignments/1.2.2", "test-token", new { paletteId = "nope" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(HttpMethod.Put, "/admin/palettes/assignments/*", "test-token", new { paletteId = created.Id }));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(HttpMethod.Put, "/admin/palettes/assignments/1.2.2", "test-token", new { }));
        Assert.Equal(HttpStatusCode.Unauthorized, await Status(HttpMethod.Get, "/admin/palettes", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Status(HttpMethod.Post, "/admin/palettes", "wrong", Body("Ok")));
        Assert.Equal(HttpStatusCode.Unauthorized, await Status(HttpMethod.Delete, $"/admin/palettes/{created.Id}", "wrong"));
    }

    [Fact]
    public async Task LowContrast_IsNotRejected()
    {
        var palette = Good();
        palette["GoFg"] = "#7f2a17";   // barely different from GoBg
        using var factory = new DotWatcherApiFactory { Palette = palette };
        using var client = factory.CreateClient();
        var alice = await AuthTestHelpers.RegisterWithResponseAsync(client, "alice", "AL");

        var me = await GetMeAsync(client, alice.AccessToken);
        var saved = await SendAdminAsync(client, HttpMethod.Post, "/admin/palettes", "test-token", Body("Poor", goFg: "#7f2a17"));

        Assert.NotNull(me.Config?.Palette);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
    }

    // A valid palette as the API takes it (camelCase JSON); override a colour or two as needed.
    private static object Body(string? name = "Test", string? label = null, string goBg = "#6f1a07", string goFg = "#f7f3e3") => new
    {
        version = 1, name, label,
        goBg, goFg, goBar = "#af9164",
        altBg = "#f7f3e3", altFg = "#2b2118",
        accent = "#af9164", accentInk = "#2b2118",
        screen = "#ffffff",
    };

    private static async Task<PaletteEntry> CreateAsync(HttpClient client, string name, string? label = null, string goBg = "#6f1a07", string goFg = "#f7f3e3")
    {
        var response = await SendAdminAsync(client, HttpMethod.Post, "/admin/palettes", "test-token", Body(name, label, goBg, goFg));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaletteEntry>())!;
    }

    private static Task<HttpResponseMessage> SendAdminAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static async Task<AuthenticatedUser> GetMeAsync(HttpClient client, string token, string? appVersion = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (appVersion is not null)
            request.Headers.Add("X-App-Version", appVersion);
        var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<AuthenticatedUser>())!;
    }
}
