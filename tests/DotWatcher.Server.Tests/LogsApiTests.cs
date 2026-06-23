using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class LogsApiTests
{
    [Fact]
    public async Task GetLog_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLog_WithInvalidBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendLogWithBearerAsync(client, HttpMethod.Get, bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLog_WithBearerToken_ReturnsBufferedApplicationLogs()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            token);

        var response = await SendLogWithBearerAsync(client, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lines = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(lines);
        Assert.Contains(lines, line => line.Contains(session.SessionId) && line.Contains("Alice"));
        Assert.DoesNotContain(lines, line => line.Contains("-41.17") || line.Contains("174.7762"));
    }

    [Fact]
    public async Task ClearLog_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClearLog_WithInvalidBearerToken_ReturnsUnauthorized()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await SendLogWithBearerAsync(client, HttpMethod.Delete, bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClearLog_WithBearerToken_ClearsBufferedLogs()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        var token = await AuthTestHelpers.RegisterAsync(client, "alice", "Alice");
        var session = await AuthTestHelpers.CreateSessionAsync(client, token);

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Ignored", session.SessionId),
            token);

        var cleared = await SendLogWithBearerAsync(client, HttpMethod.Delete);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var logResponse = await SendLogWithBearerAsync(client, HttpMethod.Get);
        var lines = await logResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(lines);
        Assert.Empty(lines);
    }

    private static async Task<HttpResponseMessage> SendLogWithBearerAsync(
        HttpClient client,
        HttpMethod method,
        string bearerToken = "test-token")
    {
        using var request = new HttpRequestMessage(method, "/log");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        return await client.SendAsync(request);
    }
}
