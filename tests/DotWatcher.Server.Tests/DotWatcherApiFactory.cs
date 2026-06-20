using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DotWatcher.Server.Tests;

public sealed class DotWatcherApiFactory : WebApplicationFactory<global::Program>
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"dotwatcher-tests-{Guid.NewGuid():N}.db");

    private readonly string _recordingsPath = Path.Combine(
        Path.GetTempPath(),
        $"dotwatcher-recordings-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BearerToken"] = "test-token",
                ["JwtSigningKey"] = "test-jwt-signing-key-change-me-32-bytes",
                ["DbPath"] = _dbPath,
                ["RecordingsPath"] = _recordingsPath,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        TryDelete(_dbPath);
        TryDelete($"{_dbPath}-shm");
        TryDelete($"{_dbPath}-wal");

        TryDeleteDirectory(_recordingsPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
