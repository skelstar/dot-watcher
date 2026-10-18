using Microsoft.AspNetCore.Mvc;

namespace DotWatcher.Server.Controllers;

[ApiController]
public class ClientErrorsController(
    ILogger<ClientErrorsController> logger,
    ClientErrorLimiter limiter) : ControllerBase
{
    private const int MaxStackTraceLength = 8_000;
    private const int MaxFieldLength = 200;

    /// <summary>
    /// Receives a crash report from a client app, after the crash, on its next launch, and writes it to
    /// the log (Seq) at error level. Unauthenticated on purpose: a crash can happen before anyone has
    /// signed in. That is why input is capped and rate-limited (<see cref="ClientErrorLimiter"/>) and
    /// nothing from the body is trusted beyond being logged as text.
    /// </summary>
    [HttpPost("/client-errors")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Report([FromBody] ClientErrorReport report)
    {
        if (string.IsNullOrWhiteSpace(report.StackTrace))
            return BadRequest(new { error = "StackTrace is required." });

        Request.Headers.TryGetValue("X-Device-Id", out var deviceId);
        if (!limiter.TryAccept(deviceId.ToString()))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many reports." });

        var stackTrace = Truncate(report.StackTrace, MaxStackTraceLength)!;
        // The first line ("java.lang.NoSuchMethodError: ...") is what you scan in Seq; the full
        // trace rides along as a property.
        var summary = Truncate(stackTrace.Split('\n', 2)[0].Trim(), MaxFieldLength);

        Request.Headers.TryGetValue("X-Client-Id", out var clientId);
        Request.Headers.TryGetValue("X-Device-Name", out var deviceName);

        // One error-level event. The scope attaches the structured fields to it (and only to it), so
        // Seq shows a scannable one-line message with the full trace and device details as properties.
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CrashStackTrace"] = stackTrace,
            ["CrashThread"] = Truncate(report.ThreadName, MaxFieldLength),
            ["CrashOccurredAt"] = report.OccurredAt,
            ["CrashOccurrences"] = report.Occurrences,
            ["ReportedOsVersion"] = Truncate(report.OsVersion, MaxFieldLength),
            ["ReportedDeviceModel"] = Truncate(report.DeviceModel, MaxFieldLength),
            ["ReportedDeviceName"] = Truncate(deviceName.ToString(), MaxFieldLength),
            ["ReportedDeviceId"] = Truncate(deviceId.ToString(), MaxFieldLength),
        }))
        {
            logger.LogError(
                "Client crash ({ClientId} {ReportedAppVersion}): {CrashSummary}",
                Truncate(clientId.ToString(), MaxFieldLength),
                Truncate(report.AppVersion, MaxFieldLength),
                summary);
        }

        return NoContent();
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
