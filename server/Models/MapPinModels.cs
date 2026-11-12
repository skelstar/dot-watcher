namespace DotWatcher.Server;

// Message pins: a runner presses and holds on the map, types a short message, and a pin goes there.
// In a named session everyone in it sees the pin; on a casual run the runner's crew and anyone with
// their watch link do. See .ai/plans/session-map-pins.md.

/// <summary>
/// A pin as one viewer sees it. <see cref="Author"/> is the name the viewer knows the author by (their
/// session name, or the crew label used on the casual map). <see cref="IsMine"/> and
/// <see cref="CanDelete"/> are worked out for the viewer, so clients don't need user ids.
/// </summary>
public record MapMessagePin(
    string Id,
    string Author,
    double Latitude,
    double Longitude,
    string Message,
    DateTimeOffset CreatedAt,
    bool IsMine = false,
    bool CanDelete = false
);

public record CreateMapPinRequest(double? Latitude, double? Longitude, string? Message);

public static class MapPinRules
{
    public const int MaxMessageLength = 140;
    /// <summary>Most pins one session, or one casual run, can hold.</summary>
    public const int MaxPins = 50;

    /// <summary>The trimmed message, or an error for the caller.</summary>
    public static (string? Message, string? Error) Validate(CreateMapPinRequest? request)
    {
        if (request?.Latitude is not { } lat || request.Longitude is not { } lon
            || double.IsNaN(lat) || double.IsNaN(lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
            return (null, "Invalid position.");

        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            return (null, "Write a message for the pin.");
        if (message.Length > MaxMessageLength)
            return (null, $"Keep the message to {MaxMessageLength} characters.");
        return (message, null);
    }
}

public enum MapPinCreateOutcome
{
    Created,
    // Casual: no open run to put the pin on.
    NoRun,
    // Already at MapPinRules.MaxPins.
    Full,
}

public record MapPinCreateResult(MapPinCreateOutcome Outcome, MapMessagePin? Pin = null);

/// <summary>A casual run's pin as stored, before it is labelled for a viewer.</summary>
public record StoredCasualPin(string Id, string RunId, double Latitude, double Longitude, string Message, DateTimeOffset CreatedAt);
