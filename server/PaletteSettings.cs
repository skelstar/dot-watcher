using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DotWatcher.Server;

/// <summary>
/// The start-screen colours the apps read instead of shipping a release. Every colour is a finished
/// 6-digit hex string. See .ai/plans/remote-palette.md.
/// </summary>
public record Palette(
    int Version,
    string GoBg,
    string GoFg,
    string GoBar,
    string AltBg,
    string AltFg,
    string Accent,
    string AccentInk,
    string Screen,
    // A name or version for this palette, e.g. "1.2.2" or "2026-11-b". Only a label: it is shown in logs and
    // returned to clients, and does not change what is served. Not the schema Version above.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null,
    // A display name for this palette, e.g. "Navy and orange". Like Label, only informational.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null
);

/// <summary>Server-driven settings returned with the user. A later setting is another property here.</summary>
public record UserConfig(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Palette? Palette = null
);

/// <summary>A saved palette and the app versions it is assigned to ("fallback" is used only while no version is).</summary>
public record PaletteEntry(string Id, Palette Palette, IReadOnlyList<string> AssignedTo);

/// <summary>An app version, or "fallback", and the palette phones on it get.</summary>
public record PaletteAssignment(string AppVersion, string PaletteId);

public record AssignPaletteRequest(string? PaletteId);

public enum PaletteDeleteResult { Deleted, NotFound, InUse }

/// <summary>
/// Which palette a phone gets. Palettes are saved in the database and do nothing until an admin assigns one to an
/// app version (phones send it as <c>X-App-Version</c>, e.g. "1.2.2 (20)"), or to "fallback".
/// A phone gets, in order: the palette for its exact version, the palette for the highest assigned version, the
/// fallback one (so only while no version is assigned), the <c>Palette</c> config section, then none. A missing section or a bad hex value is rejected when saved, and ignored (with a warning logged) in
/// config, so a typo leaves the apps on their built-in colours. Contrast is not checked: the playground shows the
/// ratio as advice, and choosing readable colours is up to whoever saves the palette.
/// </summary>
public sealed partial class PaletteProvider
{
    public const int CurrentVersion = 1;
    public const int MaxLabelLength = 32;
    public const int MaxNameLength = 30;

    /// <summary>What the API calls the fallback assignment; it is stored under <see cref="FallbackKey"/>.</summary>
    public const string FallbackRoute = "fallback";
    private const string FallbackKey = "*";
    private const long CacheMs = 5000;
    private const long Stale = -2 * CacheMs; // TickCount64 is never negative, so this always reads as expired
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SessionStore _store;
    private readonly ILogger<PaletteProvider> _logger;
    private readonly Palette? _configured;
    private volatile IReadOnlyDictionary<string, Palette>? _assigned;
    private long _assignedAt = Stale;

    public PaletteProvider(IConfiguration configuration, SessionStore store, ILogger<PaletteProvider> logger)
    {
        _store = store;
        _logger = logger;
        var section = configuration.GetSection("Palette");
        if (!section.Exists())
            return;

        var candidate = new Palette(
            section.GetValue("Version", CurrentVersion),
            section["GoBg"] ?? "",
            section["GoFg"] ?? "",
            section["GoBar"] ?? "",
            section["AltBg"] ?? "",
            section["AltFg"] ?? "",
            section["Accent"] ?? "",
            section["AccentInk"] ?? "",
            section["Screen"] ?? "",
            section["Label"],
            section["Name"]);

        var problem = Validate(candidate);
        if (problem is null)
            _configured = candidate;
        else
            logger.LogWarning("Palette config ignored, serving no palette: {Problem}", problem);
    }

    /// <summary>The palette for a phone reporting this <c>X-App-Version</c> header, or null for the built-in colours.</summary>
    public Palette? For(string? appVersionHeader)
    {
        var assigned = ReadAssigned();
        var version = AppVersionName(appVersionHeader);
        if (version is not null && assigned.TryGetValue(version, out var forVersion))
            return forVersion;
        if (Latest(assigned.Keys) is { } latest)
            return assigned[latest];
        return assigned.TryGetValue(FallbackKey, out var fallback) ? fallback : _configured;
    }

    /// <summary>The highest assigned app version, compared number by number ("1.10.0" beats "1.9.2"); null when none is.</summary>
    private static string? Latest(IEnumerable<string> versions)
    {
        string? best = null;
        int[]? bestParts = null;
        foreach (var version in versions)
        {
            if (VersionParts(version) is not { } parts)
                continue;
            if (bestParts is null || Compare(parts, bestParts) > 0 || (Compare(parts, bestParts) == 0 && string.CompareOrdinal(version, best) > 0))
                (best, bestParts) = (version, parts);
        }
        return best;
    }

    // "1.4.2" as [1, 4, 2]; null for anything that isn't dot-separated numbers (including the fallback key).
    private static int[]? VersionParts(string version)
    {
        var pieces = version.Split('.');
        var parts = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
            if (!int.TryParse(pieces[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parts[i]))
                return null;
        return parts;
    }

    // Missing trailing numbers count as 0, so "1.4" and "1.4.0" are equal.
    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var diff = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (diff != 0)
                return diff;
        }
        return 0;
    }

    /// <summary>"1.2.2" from "1.2.2 (20)"; null when the header is missing.</summary>
    public static string? AppVersionName(string? header) =>
        header?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var name, ..]
            ? name
            : null;

    public IReadOnlyList<PaletteEntry> List()
    {
        var assignments = Assignments();
        return _store.GetPalettes()
            .Select(row => ToEntry(row.Id, row.Json, assignments))
            .OfType<PaletteEntry>()
            .OrderBy(e => e.Palette.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PaletteEntry? Get(string id) =>
        _store.GetPaletteJson(id) is { } json ? ToEntry(id, json, Assignments()) : null;

    /// <summary>Saves a new palette. Returns what was wrong, or null; it serves nobody until assigned.</summary>
    public string? Create(Palette palette, out PaletteEntry? entry)
    {
        entry = null;
        var problem = Prepare(palette, out var clean);
        if (problem is not null)
            return problem;

        var id = Guid.NewGuid().ToString("N")[..12];
        _store.InsertPalette(id, JsonSerializer.Serialize(clean, Json));
        _logger.LogInformation("Palette {Id} created by admin, name {Name}, label {Label}", id, clean.Name, clean.Label ?? "(none)");
        entry = new PaletteEntry(id, clean, []);
        return null;
    }

    /// <summary>Replaces a saved palette. Phones it is assigned to get the change within a few seconds.</summary>
    public string? Update(string id, Palette palette, out PaletteEntry? entry)
    {
        entry = null;
        var problem = Prepare(palette, out var clean);
        if (problem is not null)
            return problem;

        if (!_store.UpdatePalette(id, JsonSerializer.Serialize(clean, Json)))
            return null;

        _assignedAt = Stale;
        _logger.LogInformation("Palette {Id} updated by admin, name {Name}, label {Label}", id, clean.Name, clean.Label ?? "(none)");
        entry = Get(id);
        return null;
    }

    /// <summary>Deletes a palette, unless it is assigned to a version.</summary>
    public PaletteDeleteResult Delete(string id) => _store.DeletePalette(id);

    public IReadOnlyList<PaletteAssignment> Assignments() =>
        _store.GetPaletteAssignments()
            .Select(a => new PaletteAssignment(a.AppVersion == FallbackKey ? FallbackRoute : a.AppVersion, a.PaletteId))
            .OrderBy(a => a.AppVersion, StringComparer.Ordinal)
            .ToList();

    /// <summary>Gives the palette to phones on this app version ("fallback" for the rest). False when the palette does not exist.</summary>
    public bool Assign(string appVersion, string paletteId)
    {
        if (_store.GetPaletteJson(paletteId) is null)
            return false;

        _store.AssignPalette(Key(appVersion), paletteId);
        _assignedAt = Stale;
        _logger.LogInformation("Palette {Id} assigned to {AppVersion}", paletteId, appVersion);
        return true;
    }

    public bool Unassign(string appVersion)
    {
        var removed = _store.UnassignPalette(Key(appVersion));
        _assignedAt = Stale;
        return removed;
    }

    /// <summary>An app version, or "fallback", as the API takes it: a short, plain token.</summary>
    public static bool IsValidRoute(string appVersion) => RouteToken().IsMatch(appVersion);

    private static string Key(string appVersion) => appVersion == FallbackRoute ? FallbackKey : appVersion;

    // A saved palette needs a name so the list is usable, and is trimmed so storage is stable.
    private static string? Prepare(Palette palette, out Palette clean)
    {
        clean = palette with
        {
            Label = palette.Label?.Trim() is { Length: > 0 } label ? label : null,
            Name = palette.Name?.Trim() is { Length: > 0 } name ? name : null,
        };
        return clean.Name is null ? "Name is required" : Validate(clean);
    }

    private PaletteEntry? ToEntry(string id, string json, IReadOnlyList<PaletteAssignment> assignments)
    {
        var palette = Read(json);
        return palette is null
            ? null
            : new PaletteEntry(id, palette, assignments.Where(a => a.PaletteId == id).Select(a => a.AppVersion).ToList());
    }

    private Palette? Read(string json)
    {
        try
        {
            var candidate = JsonSerializer.Deserialize<Palette>(json, Json);
            var problem = candidate is null ? "empty" : Validate(candidate);
            if (problem is null)
                return candidate;
            _logger.LogWarning("Stored palette ignored: {Problem}", problem);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Stored palette ignored, unreadable: {Message}", ex.Message);
        }
        return null;
    }

    // App version (or "*") to palette, cached for a few seconds so login and GET /me don't each read the database.
    private IReadOnlyDictionary<string, Palette> ReadAssigned()
    {
        var cached = _assigned;
        if (cached is not null && Environment.TickCount64 - _assignedAt < CacheMs)
            return cached;

        var fresh = new Dictionary<string, Palette>();
        foreach (var (version, json) in _store.GetAssignedPaletteJson())
            if (Read(json) is { } palette)
                fresh[version] = palette;

        _assigned = fresh;
        _assignedAt = Environment.TickCount64;
        return fresh;
    }

    /// <summary>The first thing wrong with a palette, or null when it is fine to serve.</summary>
    public static string? Validate(Palette palette)
    {
        if (palette.Version != CurrentVersion)
            return $"unsupported version {palette.Version}";

        if (palette.Label is { } label && (label.Length > MaxLabelLength || label.Any(char.IsControl)))
            return $"Label must be {MaxLabelLength} characters or fewer with no control characters";

        if (palette.Name is { } paletteName && (paletteName.Length > MaxNameLength || paletteName.Any(char.IsControl)))
            return $"Name must be {MaxNameLength} characters or fewer with no control characters";

        var colours = new (string Name, string Value)[]
        {
            (nameof(palette.GoBg), palette.GoBg), (nameof(palette.GoFg), palette.GoFg),
            (nameof(palette.GoBar), palette.GoBar), (nameof(palette.AltBg), palette.AltBg),
            (nameof(palette.AltFg), palette.AltFg), (nameof(palette.Accent), palette.Accent),
            (nameof(palette.AccentInk), palette.AccentInk), (nameof(palette.Screen), palette.Screen),
        };
        foreach (var (name, value) in colours)
            if (value is null || !HexColour().IsMatch(value))
                return $"{name} is not a #rrggbb colour";

        return null;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.+_-]{0,31}$")]
    private static partial Regex RouteToken();
}
