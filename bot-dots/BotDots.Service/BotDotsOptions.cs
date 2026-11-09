namespace BotDots.Service;

/// <summary>Configuration, bound from the <c>BotDots</c> section (environment: <c>BotDots__AdminBearer</c> etc.).</summary>
public sealed class BotDotsOptions
{
    public const string SectionName = "BotDots";

    /// <summary>The DotWatcher server. Run against ONE server only: Staging and Production share a database.</summary>
    public string ServerUrl { get; set; } = "http://localhost:8080";

    /// <summary>The server's admin <c>BearerToken</c>. Needed to read spawn cells and to create bot-dot accounts. Required.</summary>
    public string AdminBearer { get; set; } = "";

    /// <summary>A Valhalla server. The public demo (valhalla1.openstreetmap.de) is fine for a spike, not for running this.</summary>
    public string ValhallaUrl { get; set; } = "http://valhalla:8002";

    /// <summary>Minimum gap between Valhalla requests, for a rate-limited public server. 0 for a self-hosted one.</summary>
    public int ValhallaMinIntervalMs { get; set; }

    /// <summary>Most bot-dots out at once, across all cells. Also the size of the account pool (botdot-001...).</summary>
    public int MaxBots { get; set; } = 40;

    /// <summary>Fill a cell with bot-dots until it has this many dots, real people and bot-dots together.</summary>
    public int TargetPerCell { get; set; } = 3;

    public int MaxPerCell { get; set; } = 5;

    /// <summary>Stagger: at most this many new bot-dots per cell per sync.</summary>
    public int MaxSpawnPerCellPerSync { get; set; } = 2;

    /// <summary>A new bot-dot starts this far from the middle of the cell (it only ever knows the cell, never a person).</summary>
    public double SpawnMinM { get; set; } = 300;

    public double SpawnMaxM { get; set; } = 1800;

    /// <summary>A bot-dot's routes stay within this distance of the cell's middle.</summary>
    public double MaxDistanceFromCentreM { get; set; } = 2000;

    public double HopMinM { get; set; } = 400;

    public double HopMaxM { get; set; } = 1200;

    public int PostIntervalSeconds { get; set; } = 15;

    public int SyncIntervalSeconds { get; set; } = 30;

    public int TripMinMinutes { get; set; } = 20;

    public int TripMaxMinutes { get; set; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AdminBearer))
            throw new InvalidOperationException("BotDots:AdminBearer is not configured (set BotDots__AdminBearer).");
        if (MaxBots < 1 || TargetPerCell < 1 || MaxPerCell < 1 || PostIntervalSeconds < 1 || SyncIntervalSeconds < 1)
            throw new InvalidOperationException("BotDots counts and intervals must be at least 1.");
        if (HopMinM <= 0 || HopMaxM < HopMinM || SpawnMaxM < SpawnMinM || TripMaxMinutes < TripMinMinutes)
            throw new InvalidOperationException("BotDots min/max settings are inconsistent.");
    }
}
