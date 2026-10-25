using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace DotWatcher.Server;

// Watch links: a secret link to one casual run, for someone with no account (and no app) to watch it
// on the web. The link belongs to the run: it works while the run is open and says "ended" afterwards.
// Only a hash of the secret is stored. See .ai/plans/casual-tracking.md.
public partial class SessionStore
{
    // 16 random bytes, base64url: 22 characters, 128 bits.
    public const int WatchTokenLength = 22;

    private static string HashWatchToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>True for a string shaped like a watch token, so anything else is refused before a lookup.</summary>
    public static bool LooksLikeWatchToken(string? value) =>
        value is { Length: WatchTokenLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Makes a new link for the caller's open run, or null when they aren't out. Every call makes a
    /// fresh link (the secret is only ever shown once); all of a run's links end with the run, and
    /// <see cref="RevokeCasualWatchLinks"/> ends them sooner.
    /// </summary>
    public string? CreateCasualWatchLink(string userId, DateTimeOffset now)
    {
        using var conn = Connect();
        var run = FindOpenCasualRun(conn, userId);
        if (run is null)
            return null;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO casual_watch_links (token_hash, run_id, created_at) VALUES (@hash, @runId, @createdAt)";
        cmd.Parameters.AddWithValue("@hash", HashWatchToken(token));
        cmd.Parameters.AddWithValue("@runId", run.Id);
        cmd.Parameters.AddWithValue("@createdAt", ToIso(now));
        cmd.ExecuteNonQuery();
        return token;
    }

    /// <summary>Ends every watch link for the caller's open run. Safe to repeat.</summary>
    public void RevokeCasualWatchLinks(string userId)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM casual_watch_links
            WHERE run_id IN (SELECT id FROM casual_runs WHERE user_id = @userId AND ended_at IS NULL)
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>What a watch link shows, or null for a link that doesn't exist (or was revoked).</summary>
    public CasualWatchView? GetCasualWatchView(string token, DateTimeOffset now)
    {
        using var conn = Connect();

        string runId;
        bool ended;
        string label;
        using (var lookup = conn.CreateCommand())
        {
            lookup.CommandText = """
                SELECT r.id, r.ended_at, u.display_name
                FROM casual_watch_links w
                JOIN casual_runs r ON r.id = w.run_id
                JOIN users u ON u.id = r.user_id
                WHERE w.token_hash = @hash
                """;
            lookup.Parameters.AddWithValue("@hash", HashWatchToken(token));
            using var reader = lookup.ExecuteReader();
            if (!reader.Read())
                return null;
            runId = reader.GetString(0);
            ended = !reader.IsDBNull(1);
            label = reader.GetString(2);
        }

        if (ended)
            return new CasualWatchView(label, Ended: true, Live: false, Position: null);

        using var latest = conn.CreateCommand();
        latest.CommandText = """
            SELECT latitude, longitude, heading, timestamp, received_at, next_expected_at
            FROM location_updates
            WHERE casual_run_id = @runId AND received_at IS NOT NULL
            ORDER BY received_at DESC
            LIMIT 1
            """;
        latest.Parameters.AddWithValue("@runId", runId);
        using var row = latest.ExecuteReader();
        if (!row.Read())
            return new CasualWatchView(label, Ended: false, Live: false, Position: null);

        var position = new RunnerPosition(
            label,
            row.GetDouble(0),
            row.GetDouble(1),
            row.IsDBNull(2) ? null : row.GetDouble(2),
            DateTimeOffset.Parse(row.GetString(3)),
            row.IsDBNull(5) ? null : DateTimeOffset.Parse(row.GetString(5)));
        var receivedAt = DateTimeOffset.Parse(row.GetString(4));
        var live = CasualVisibility.IsLive(
            new CasualLiveEntry("", position, InSession: false, VisibleToNonCrew: false, null, null, receivedAt), now);

        return new CasualWatchView(
            label,
            Ended: false,
            live,
            new WatchPosition(position.Latitude, position.Longitude, position.Heading, position.Timestamp, receivedAt));
    }
}
