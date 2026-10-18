using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DotWatcher.Server.Tests;

public class ClientErrorsApiTests
{
    private static ClientErrorReport Report(string? stackTrace = "java.lang.IllegalStateException: boom\n\tat a.b.C.d(C.kt:1)") =>
        new(stackTrace, "main", DateTimeOffset.UtcNow, 1, "0.2.3 (6)", "Android 14 (API 34)", "SM-S911B");

    [Fact]
    public async Task Report_WithoutAnySignIn_IsAccepted()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/client-errors", Report());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Report_WithoutAStackTrace_ReturnsBadRequest()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/client-errors", Report(stackTrace: "  "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Report_WithAnOversizedStackTrace_IsStillAccepted()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/client-errors", Report(new string('x', 200_000)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Report_FromOneDevice_IsRateLimited()
    {
        using var factory = new DotWatcherApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Id", "crash-looping-device");

        HttpStatusCode last = HttpStatusCode.NoContent;
        for (var i = 0; i < 11; i++)
            last = (await client.PostAsJsonAsync("/client-errors", Report())).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}
