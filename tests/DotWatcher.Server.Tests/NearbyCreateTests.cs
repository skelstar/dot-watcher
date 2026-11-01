using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

// Creating a session that people nearby can find: title, radius and meeting-point validation.
// See .ai/plans/nearby-join.md (tasks N1, N2).
public class NearbyCreateTests
{
    private static object Valid(object? title = null, int? radius = null, double? accuracy = 12) => new
    {
        sessionName = title ?? "Sunday long run",
        nearby = true,
        nearbyRadiusMeters = radius,
        latitude = -36.85,
        longitude = 174.76,
        accuracy,
    };

    [Fact]
    public async Task Create_Nearby_KeepsTheTitleAsTypedAndStillGeneratesAnInviteCode()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await CreateAsync(client, token, Valid());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<SessionMembership>();
        Assert.NotNull(session);
        Assert.Equal("Sunday long run", session.SessionName);
        Assert.Equal(6, session.InviteCode.Length);
    }

    [Fact]
    public async Task Create_Nearby_WithoutTheEventsFeature_IsRejected()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = [] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await CreateAsync(client, token, Valid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This title is far too long to show a stranger")]
    public async Task Create_Nearby_RejectsMissingOrOverlongTitle(string title)
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await CreateAsync(client, token, Valid(title));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(1000)]
    public async Task Create_Nearby_RejectsRadiusOutsideTheFourChoices(int radius)
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await CreateAsync(client, token, Valid(radius: radius));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(500)]
    public async Task Create_Nearby_AcceptsEachAllowedRadius(int radius)
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var response = await CreateAsync(client, token, Valid(radius: radius));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_Nearby_RequiresAUsablePosition()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var noPosition = await CreateAsync(client, token, new { sessionName = "Sunday run", nearby = true });
        var outOfRange = await CreateAsync(client, token,
            new { sessionName = "Sunday run", nearby = true, latitude = 123.0, longitude = 174.76 });
        var poorFix = await CreateAsync(client, token, Valid(accuracy: 250));

        Assert.Equal(HttpStatusCode.BadRequest, noPosition.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, poorFix.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutNearby_KeepsTheLegacyNameRule()
    {
        using var factory = new DotWatcherApiFactory { EnabledForAll = ["events"] };
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");

        var tooLong = await CreateAsync(client, token, new { sessionName = "Sunday long run" });
        var legacy = await CreateAsync(client, token, new { sessionName = "sunset23" });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("SUNSET23", (await legacy.Content.ReadFromJsonAsync<SessionMembership>())!.SessionName);
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
