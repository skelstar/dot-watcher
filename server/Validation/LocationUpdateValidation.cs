namespace DotWatcher.Server;

public sealed class LocationUpdateValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record ValidatedLocationUpdate(
    string RunnerName,
    string SessionId,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp,
    DateTimeOffset? NextExpectedAt = null,
    bool IsUltraConstrained = false);

public static class LocationUpdateValidation
{
    // Loose upper bound on how far ahead a client may claim its next post will be - generous
    // enough to cover any plausible cadence (including satellite's 90s) with headroom, while
    // still catching a garbled/bogus value (e.g. wrong units, year in the far future).
    private static readonly TimeSpan MaxNextExpectedAtLookahead = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<string> Validate(LocationUpdate update)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(update.RunnerName))
            errors.Add("Runner name is required.");

        if (string.IsNullOrWhiteSpace(update.SessionId) || !Guid.TryParse(update.SessionId, out _))
            errors.Add("Session ID is required.");

        if (update.Latitude is not { } latitude || !double.IsFinite(latitude) || latitude is < -90 or > 90)
            errors.Add("Latitude must be between -90 and 90.");

        if (update.Longitude is not { } longitude || !double.IsFinite(longitude) || longitude is < -180 or > 180)
            errors.Add("Longitude must be between -180 and 180.");

        if (update.Heading is { } heading && (!double.IsFinite(heading) || heading is < 0 or > 360))
            errors.Add("Heading must be between 0 and 360.");

        if (update.Timestamp is null || update.Timestamp == default(DateTimeOffset))
            errors.Add("Timestamp is required.");

        // Optional - older clients don't send it. When present, it must be a real future moment
        // relative to the position it accompanies, not wildly so.
        if (update.NextExpectedAt is { } nextExpectedAt)
        {
            var timestamp = update.Timestamp ?? DateTimeOffset.UtcNow;
            if (nextExpectedAt <= timestamp)
                errors.Add("nextExpectedAt must be after timestamp.");
            else if (nextExpectedAt - timestamp > MaxNextExpectedAtLookahead)
                errors.Add($"nextExpectedAt must be within {MaxNextExpectedAtLookahead.TotalMinutes:0} minutes of timestamp.");
        }

        return errors;
    }

    public static bool TryValidate(
        LocationUpdate update,
        out ValidatedLocationUpdate? validated,
        out IReadOnlyList<string> errors)
    {
        errors = Validate(update);
        if (errors.Count > 0)
        {
            validated = null;
            return false;
        }

        validated = new ValidatedLocationUpdate(
            update.RunnerName!,
            update.SessionId!,
            update.Latitude!.Value,
            update.Longitude!.Value,
            update.Heading,
            update.Timestamp!.Value,
            update.NextExpectedAt,
            update.IsUltraConstrained);
        return true;
    }
}
