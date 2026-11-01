namespace DotWatcher.Server;

/// <summary>Names of the features that can be switched on per account. See .ai/plans/crew-feature-flag.md.</summary>
public static class Features
{
    public static readonly IReadOnlyList<string> All = [Crew, Casual, Events];

    public const string Crew = "crew";
    public const string Casual = "casual";
    public const string Events = "events";
}

/// <summary>
/// Which features an account can use: the ones granted to that account (stored on the user row)
/// plus any listed in the <c>Features:EnabledForAll</c> config array, which is how a feature goes
/// general without touching every account.
/// </summary>
public sealed class FeatureFlags
{
    private readonly SessionStore _store;
    private readonly IReadOnlyList<string> _enabledForAll;
    private const long SwitchCacheMs = 5000;
    private volatile IReadOnlyList<string>? _switchedOff;
    private long _switchedOffAt;

    public FeatureFlags(IConfiguration configuration, SessionStore store)
    {
        _store = store;
        _enabledForAll = Normalize(configuration.GetSection("Features:EnabledForAll").Get<string[]>());
    }

    public IReadOnlyList<string> ForUser(string userId) =>
        Normalize(_enabledForAll.Concat(_store.GetUserFeatures(userId)));

    /// <summary>
    /// The features for an account whose granted features the caller has already read, so a request
    /// that needs the account row anyway doesn't read it a second time.
    /// </summary>
    public IReadOnlyList<string> ForGranted(IEnumerable<string> granted) =>
        Normalize(_enabledForAll.Concat(granted));

    /// <summary>
    /// Whether a feature is switched on globally. Everything is on unless an admin has switched it off
    /// (PUT /admin/features/{feature}); this is the kill switch and it overrides per-account grants.
    /// The answer is cached for a few seconds so hot paths like POST /location don't read the database.
    /// </summary>
    public bool IsOn(string feature) => !SwitchedOff().Contains(feature);

    public IReadOnlyList<string> SwitchedOff()
    {
        var cached = _switchedOff;
        if (cached is not null && Environment.TickCount64 - _switchedOffAt < SwitchCacheMs)
            return cached;

        var fresh = _store.GetSwitchedOffFeatures();
        _switchedOff = fresh;
        _switchedOffAt = Environment.TickCount64;
        return fresh;
    }

    public void SetSwitch(string feature, bool enabled)
    {
        _store.SetFeatureSwitch(feature, enabled);
        _switchedOff = null;
    }

    public bool IsEnabled(string userId, string feature) =>
        _enabledForAll.Contains(feature) || _store.GetUserFeatures(userId).Contains(feature);

    /// <summary>Trims, lower-cases, drops blanks and duplicates, and sorts, so storage is stable.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? features) =>
        (features ?? [])
            .Select(f => f?.Trim().ToLowerInvariant() ?? "")
            .Where(f => f.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
}
