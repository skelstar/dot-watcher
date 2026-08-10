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

    private readonly string _schema;
    private readonly bool _ownsSchema;

    private readonly string _recordingsPath = Path.Combine(
        Path.GetTempPath(),
        $"dotwatcher-recordings-{Guid.NewGuid():N}");

    /// <summary>Schema backing this factory's database, so a second factory can be pointed at it.</summary>
    public string Schema => _schema;

    public DotWatcherApiFactory() : this($"test_{Guid.NewGuid():N}", ownsSchema: true)
    {
    }

    /// <summary>
    /// Points at a schema already created by another <see cref="DotWatcherApiFactory"/> (pass its
    /// <see cref="Schema"/>) instead of creating a new one. Lets two factories simulate two
    /// separate server processes sharing one database - e.g. Staging and Production after the
    /// shared-Postgres cutover, where a phone's POST can land on one pod while a viewer polls the
    /// other. This instance doesn't create or drop the schema; the owning factory does both.
    /// </summary>
    public DotWatcherApiFactory(string schema) : this(schema, ownsSchema: false)
    {
    }

    private DotWatcherApiFactory(string schema, bool ownsSchema)
    {
        _schema = schema;
        _ownsSchema = ownsSchema;
        if (!ownsSchema)
            return;

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

        if (_ownsSchema)
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
