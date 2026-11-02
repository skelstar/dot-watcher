using System.Globalization;
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

/// <summary>
/// The palette to serve. One set by an admin (POST /admin/palette, stored in the database) wins over the
/// <c>Palette</c> config section, which is read once at start-up. A missing section, a bad hex value or a
/// text/background pair under 4.5:1 contrast means no palette from that source (with a warning logged), so
/// a typo leaves the apps on their built-in colours rather than unreadable buttons.
/// </summary>
public sealed partial class PaletteProvider
{
    public const int CurrentVersion = 1;
    public const double MinimumContrast = 4.5;
    public const int MaxLabelLength = 32;
    public const int MaxNameLength = 30;

    public const string SettingKey = "palette";
    private const long CacheMs = 5000;
    private const long Stale = -2 * CacheMs; // TickCount64 is never negative, so this always reads as expired
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SessionStore _store;
    private readonly ILogger<PaletteProvider> _logger;
    private readonly Palette? _configured;
    private volatile Palette? _stored;
    private long _storedAt = Stale;

    /// <summary>The admin-set palette if there is a valid one, otherwise the configured one, otherwise none.</summary>
    public Palette? Palette => ReadStored() ?? _configured;

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

    /// <summary>Stores a validated palette for everyone, replacing any earlier one. Returns what was wrong, or null.</summary>
    public string? Set(Palette palette)
    {
        var problem = Validate(palette);
        if (problem is not null)
            return problem;

        palette = palette with
        {
            Label = palette.Label?.Trim() is { Length: > 0 } label ? label : null,
            Name = palette.Name?.Trim() is { Length: > 0 } name ? name : null,
        };
        _store.SetSetting(SettingKey, JsonSerializer.Serialize(palette, Json));
        _logger.LogInformation("Palette set by admin, name {Name}, label {Label}", palette.Name ?? "(none)", palette.Label ?? "(none)");
        _storedAt = Stale;
        return null;
    }

    /// <summary>Removes the admin-set palette so the config one (if any) is served again.</summary>
    public bool Clear()
    {
        var removed = _store.DeleteSetting(SettingKey);
        _storedAt = Stale;
        return removed;
    }

    // Cached for a few seconds so login and GET /me don't each read the database.
    private Palette? ReadStored()
    {
        if (Environment.TickCount64 - _storedAt < CacheMs)
            return _stored;

        Palette? fresh = null;
        var raw = _store.GetSetting(SettingKey);
        if (raw is not null)
        {
            try
            {
                var candidate = JsonSerializer.Deserialize<Palette>(raw, Json);
                var problem = candidate is null ? "empty" : Validate(candidate);
                if (problem is null)
                    fresh = candidate;
                else
                    _logger.LogWarning("Stored palette ignored: {Problem}", problem);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Stored palette ignored, unreadable: {Message}", ex.Message);
            }
        }

        _stored = fresh;
        _storedAt = Environment.TickCount64;
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

        foreach (var (name, fg, bg) in new[]
        {
            ("GoFg on GoBg", palette.GoFg, palette.GoBg),
            ("AltFg on AltBg", palette.AltFg, palette.AltBg),
            ("AccentInk on Accent", palette.AccentInk, palette.Accent),
        })
        {
            var ratio = ContrastRatio(fg, bg);
            if (ratio < MinimumContrast)
                return $"{name} contrast {ratio:0.00}:1 is under {MinimumContrast}:1";
        }

        return null;
    }

    /// <summary>WCAG contrast ratio between two #rrggbb colours.</summary>
    public static double ContrastRatio(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
