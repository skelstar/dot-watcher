using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotWatcher.Server;

/// <summary>One entry of the activity register (Activity/activity-events.json).</summary>
public sealed record ActivityEventDefinition(
    string Kind,
    string Label,
    string Shows,
    string Status,
    string? BranchFrom = null,
    string? Route = null,
    string? Describe = null,
    string? Notes = null)
{
    [JsonIgnore]
    public bool IsRecording => Status == "recording";
}

/// <summary>
/// The activity register: every event the server records, or that someone has asked for. Embedded in
/// the assembly from Activity/activity-events.json and read once. The server records an event when a
/// successful request matches the route of a <c>recording</c> entry; <c>wanted</c> entries are ignored.
/// See .ai/plans/activity-flow.md.
/// </summary>
public static class ActivityRegister
{
    private sealed record RegisterFile(IReadOnlyList<ActivityEventDefinition> Events);

    private static readonly Lazy<IReadOnlyList<ActivityEventDefinition>> _events = new(Load);
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> _kindsByRoute = new(BuildRouteMap);

    public static IReadOnlyList<ActivityEventDefinition> Events => _events.Value;

    /// <summary>The recording kinds for a request, keyed as the register writes routes: "POST /casual/start".</summary>
    public static IReadOnlyList<string> KindsFor(string method, string? routePattern)
    {
        if (routePattern is null)
            return [];
        var key = $"{method.ToUpperInvariant()} {NormalizeRoute(routePattern)}";
        return _kindsByRoute.Value.TryGetValue(key, out var kinds) ? kinds : [];
    }

    // Attribute routes may be written with or without the leading slash.
    public static string NormalizeRoute(string routePattern) =>
        routePattern.StartsWith('/') ? routePattern : "/" + routePattern;

    private static IReadOnlyList<ActivityEventDefinition> Load()
    {
        using var stream = typeof(ActivityRegister).Assembly.GetManifestResourceStream("activity-events.json")
            ?? throw new InvalidOperationException("The activity register (activity-events.json) is not embedded in the assembly.");
        var file = JsonSerializer.Deserialize<RegisterFile>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("The activity register is empty.");
        return file.Events;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildRouteMap()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in Events.Where(e => e.IsRecording && e.Route is not null))
        {
            var space = entry.Route!.IndexOf(' ');
            if (space <= 0)
                continue;
            var key = $"{entry.Route[..space].ToUpperInvariant()} {NormalizeRoute(entry.Route[(space + 1)..].Trim())}";
            if (!map.TryGetValue(key, out var kinds))
                map[key] = kinds = [];
            kinds.Add(entry.Kind);
        }
        return map.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal);
    }
}
