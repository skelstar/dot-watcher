using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Not everyone may organise a group: a "groups" account also needs "create-groups" to create a session
// (which, for that account, is a group). Accounts without "groups" create sessions as before.
public class CreateGroupsFlagTests
{
    [Fact]
    public async Task AGroupsAccount_WithoutCreateGroups_CantCreate()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Groups] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        var response = await CreateAsync(client, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AGroupsAccount_WithCreateGroups_CanCreate()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Groups, Features.CreateGroups] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        var response = await CreateAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnAccountWithoutGroups_CreatesAsBefore()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");

        var response = await CreateAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SwitchingCreateGroupsOff_StopsGroupsAccountsCreating()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [Features.Groups, Features.CreateGroups] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "kiri", "KI");
        using (var off = new HttpRequestMessage(HttpMethod.Put, $"/admin/features/{Features.CreateGroups}")
        {
            Content = JsonContent.Create(new { enabled = false }),
        })
        {
            off.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            Assert.True((await client.SendAsync(off)).IsSuccessStatusCode);
        }

        var response = await CreateAsync(client, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = JsonContent.Create(new { sessionName = "HUNUA" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
