using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DotWatcher.Server.Tests;

public sealed class DotWatcherApiFactory : WebApplicationFactory<global::Program>
{
    // Matches the local Postgres started by `docker compose up -d` at the repo root (see
    // server/README.md's "Local Postgres" section) — dev-only credentials, not used anywhere else.
    private const string BaseConnectionString =
        "Host=localhost;Port=5432;Database=dotwatcher;Username=dotwatcher;Password=dotwatcher-dev";

    private readonly string _schema = $"test_{Guid.NewGuid():N}";

    private readonly string _recordingsPath = Path.Combine(
        Path.GetTempPath(),
        $"dotwatcher-recordings-{Guid.NewGuid():N}");

    public DotWatcherApiFactory()
    {
        using var conn = new NpgsqlConnection(BaseConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{_schema}\"";
        cmd.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BearerToken"] = "test-token",
                ["JwtSigningKey"] = "test-jwt-signing-key-change-me-32-bytes",
                ["ConnectionString"] = $"{BaseConnectionString};SearchPath={_schema}",
                ["RecordingsPath"] = _recordingsPath,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        TryDropSchema(_schema);
        TryDeleteDirectory(_recordingsPath);
    }

    private static void TryDropSchema(string schema)
    {
        try
        {
            using var conn = new NpgsqlConnection(BaseConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            cmd.ExecuteNonQuery();
        }
        catch (NpgsqlException)
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
