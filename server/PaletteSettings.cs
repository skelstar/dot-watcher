using System.Globalization;
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
    string Screen
);

/// <summary>Server-driven settings returned with the user. A later setting is another property here.</summary>
public record UserConfig(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Palette? Palette = null
);

/// <summary>
/// Reads the <c>Palette</c> config section once at start-up. A missing section, a bad hex value or a
/// text/background pair under 4.5:1 contrast means no palette is served (with a warning logged), so a
/// typo in config leaves the apps on their built-in colours rather than unreadable buttons.
/// </summary>
public sealed partial class PaletteProvider
{
    public const int CurrentVersion = 1;
    public const double MinimumContrast = 4.5;

    public Palette? Palette { get; }

    public PaletteProvider(IConfiguration configuration, ILogger<PaletteProvider> logger)
    {
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
            section["Screen"] ?? "");

        var problem = Validate(candidate);
        if (problem is null)
            Palette = candidate;
        else
            logger.LogWarning("Palette config ignored, serving no palette: {Problem}", problem);
    }

    /// <summary>The first thing wrong with a palette, or null when it is fine to serve.</summary>
    public static string? Validate(Palette palette)
    {
        if (palette.Version != CurrentVersion)
            return $"unsupported version {palette.Version}";

        var colours = new (string Name, string Value)[]
        {
            (nameof(palette.GoBg), palette.GoBg), (nameof(palette.GoFg), palette.GoFg),
            (nameof(palette.GoBar), palette.GoBar), (nameof(palette.AltBg), palette.AltBg),
            (nameof(palette.AltFg), palette.AltFg), (nameof(palette.Accent), palette.Accent),
            (nameof(palette.AccentInk), palette.AccentInk), (nameof(palette.Screen), palette.Screen),
        };
        foreach (var (name, value) in colours)
            if (!HexColour().IsMatch(value))
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
