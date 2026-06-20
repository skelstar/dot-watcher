using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class LogsApiTests
{
    [Fact]
    public async Task GetLog_ReturnsBufferedApplicationLogs()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "SUNSET23"));

        var response = await client.GetAsync("/log");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lines = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(lines);
        Assert.Contains(lines, line => line.Contains("SUNSET23") && line.Contains("Alice"));
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

        var response = await DeleteLogWithBearerAsync(client, bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClearLog_WithBearerToken_ClearsBufferedLogs()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        await LocationsApiTests.PostLocationAsync(client,
            LocationsApiTests.TestLocation("Alice", "SUNSET23"));

        var cleared = await DeleteLogWithBearerAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);

        var lines = await client.GetFromJsonAsync<List<string>>("/log");
        Assert.NotNull(lines);
        Assert.Empty(lines);
    }

    private static async Task<HttpResponseMessage> DeleteLogWithBearerAsync(
        HttpClient client,
        string bearerToken = "test-token")
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/log");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        return await client.SendAsync(request);
    }
}
