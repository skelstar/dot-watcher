namespace DotWatcher.Server;

public sealed class LocationUpdateValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record ValidatedLocationUpdate(
    string RunnerName,
    string SessionCode,
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset Timestamp);

public static class LocationUpdateValidation
{
    public static IReadOnlyList<string> Validate(LocationUpdate update)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(update.RunnerName))
            errors.Add("Runner name is required.");

        if (SessionStore.NormalizeSessionCode(update.SessionCode) is null)
            errors.Add("Session code must be 3-32 letters, numbers, dashes, or underscores.");

        if (update.Latitude is not { } latitude || !double.IsFinite(latitude) || latitude is < -90 or > 90)
            errors.Add("Latitude must be between -90 and 90.");

        if (update.Longitude is not { } longitude || !double.IsFinite(longitude) || longitude is < -180 or > 180)
            errors.Add("Longitude must be between -180 and 180.");

        if (update.Heading is { } heading && (!double.IsFinite(heading) || heading is < 0 or > 360))
            errors.Add("Heading must be between 0 and 360.");

        if (update.Timestamp is null || update.Timestamp == default(DateTimeOffset))
            errors.Add("Timestamp is required.");

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
            SessionStore.NormalizeSessionCode(update.SessionCode)!,
            update.Latitude!.Value,
            update.Longitude!.Value,
            update.Heading,
            update.Timestamp!.Value);
        return true;
    }
}
