namespace DotWatcher.Server;

/// <summary>Names of the features that can be switched on per account. See .ai/plans/crew-feature-flag.md.</summary>
public static class Features
{
    public const string Crew = "crew";
    public const string Casual = "casual";
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
