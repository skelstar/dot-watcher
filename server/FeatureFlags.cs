using System.Collections.Concurrent;

namespace DotWatcher.Server;

/// <summary>Names of the features that can be switched on per account. See .ai/plans/crew-feature-flag.md.</summary>
public static class Features
{
    public static readonly IReadOnlyList<string> All = [Crew, Casual, Events, Groups, CreateGroups];

    public const string Crew = "crew";
    public const string Casual = "casual";
    public const string Events = "events";

    // People in the same active named session see each other by name in casual (see
    // .ai/plans/go-and-groups.md, step 1). Per account: off for everyone until granted.
    public const string Groups = "groups";

    // Who may organise a group. With "groups" every session an account creates is a group, and not everyone
    // should be able to make one, so a "groups" account also needs this to create (the phones hide Create
    // without it). Accounts without "groups" create sessions as before. Per account: off until granted.
    public const string CreateGroups = "create-groups";
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

    /// <summary>
    /// Granted to this account and not switched off globally. Used on hot paths (every casual
    /// POST /location and live read), so the account's grants are cached for a short while rather than
    /// read from the database on each call: a feature that is off, or on for everyone, costs no read at
    /// all, and otherwise one read per account every <see cref="UserCacheMs"/>. An admin change to an
    /// account's grants on this server clears its entry at once (<see cref="ForgetUser"/>); another
    /// server picks it up within that time.
    /// </summary>
    public bool IsOnFor(string userId, string feature)
    {
        if (!IsOn(feature))
            return false;
        if (_enabledForAll.Contains(feature))
            return true;
        return CachedUserFeatures(userId).Contains(feature);
    }

    /// <summary>Drops the cached grants for an account, after they changed.</summary>
    public void ForgetUser(string userId) => _userFeatures.TryRemove(userId, out _);

    private const long UserCacheMs = 30_000;
    private const int MaxCachedUsers = 10_000;
    private readonly ConcurrentDictionary<string, (IReadOnlyList<string> Features, long At)> _userFeatures = new();

    private IReadOnlyList<string> CachedUserFeatures(string userId)
    {
        var now = Environment.TickCount64;
        if (_userFeatures.TryGetValue(userId, out var cached) && now - cached.At < UserCacheMs)
            return cached.Features;

        var fresh = _store.GetUserFeatures(userId);
        // Bounded: far more accounts than are out at once means the cache is simply started again.
        if (_userFeatures.Count >= MaxCachedUsers)
            _userFeatures.Clear();
        _userFeatures[userId] = (fresh, now);
        return fresh;
    }

    /// <summary>Trims, lower-cases, drops blanks and duplicates, and sorts, so storage is stable.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? features) =>
        (features ?? [])
            .Select(f => f?.Trim().ToLowerInvariant() ?? "")
            .Where(f => f.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
}
