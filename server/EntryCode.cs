using System.Security.Cryptography;
using System.Text;

namespace DotWatcher.Server;

/// <summary>
/// A group's optional entry code (see .ai/plans/go-and-groups.md, step 2). The organiser gives it to
/// entrants, so someone passing a race start can see the group's tile but can't join it. It is a
/// shared code printed on a race sheet, not a password: it is stored as typed (upper-cased) so the
/// organiser can see it again, and only ever returned to the organiser. Guessing is limited by the
/// nearby join limit (<see cref="NearbyLimiter.JoinsPerUserPerHour"/>) and the caller having to be
/// inside the group's area.
/// </summary>
public static class EntryCode
{
    public const int MinLength = 4;
    public const int MaxLength = 8;

    /// <summary>
    /// The stored form: trimmed, upper-cased, 4-8 letters A-Z or digits. Null when it isn't valid.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (code is null || code.Length is < MinLength or > MaxLength)
            return null;

        return code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9') ? code : null;
    }

    /// <summary>
    /// Whether <paramref name="given"/> is the group's code, ignoring case and surrounding spaces.
    /// A group with no code accepts anything. Compared in constant time.
    /// </summary>
    public static bool Matches(string? stored, string? given)
    {
        if (stored is null)
            return true;

        var normalized = Normalize(given);
        if (normalized is null)
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(normalized));
    }
}

/// <summary>The organiser reads or changes a group's entry code. Null (or omitted) clears it.</summary>
public record EntryCodeRequest(string? EntryCode = null);

/// <summary>What the organiser is told: the code, or null when the group has none.</summary>
public record EntryCodeStatus(string? EntryCode);

public enum EntryCodeOutcome { Ok, NotOwner, Archived }

/// <summary>A nearby join: the membership, or null with <c>WrongCode</c> saying whether the only
/// problem was a missing or wrong entry code (every other refusal stays indistinguishable).</summary>
public record NearbyJoinResult(SessionMembership? Membership, bool WrongCode = false);
