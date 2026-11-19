using System.Text.Json;
using Serilog;

namespace DotWatcher.Server;

/// <summary>
/// Called by the request-logging middleware once a request has finished: if it succeeded and its route
/// is a <c>recording</c> entry in the register, records the event on the right dot's line. Never throws.
/// </summary>
public static class ActivityCapture
{
    private static readonly JsonSerializerOptions _detailOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Capture(HttpContext ctx, int status, string? signedInUserId)
    {
        try
        {
            if (status >= 400)
                return;

            var pattern = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var kinds = ActivityRegister.KindsFor(ctx.Request.Method, pattern);
            if (kinds.Count == 0)
                return;

            var info = ActivityContext.Get(ctx);
            if (info?.Skip == true)
                return;

            // A route with several kinds (POST /me/activity) records only the one the controller chose.
            if (info?.Kind is { } chosen)
            {
                if (!kinds.Contains(chosen))
                    return;
                kinds = [chosen];
            }

            // A controller names the dot when it isn't the caller: an anonymous watch-link viewer's
            // open goes on the run owner's line.
            var userId = info?.UserId ?? signedInUserId;
            if (userId is null)
                return;

            var headers = ctx.Request.Headers;
            string? Header(string name) =>
                headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : null;

            var detail = info?.Detail is null ? null : JsonSerializer.Serialize(info.Detail, _detailOptions);
            var recorder = ctx.RequestServices.GetRequiredService<ActivityRecorder>();
            var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();

            foreach (var kind in kinds)
            {
                recorder.Record(new ActivityEvent(
                    now,
                    userId,
                    kind,
                    DeviceId: Header("X-Device-Id"),
                    Client: Header("X-Client-Id"),
                    AppVersion: Header("X-App-Version"),
                    RunId: info?.RunId,
                    SessionId: ctx.GetRouteValue("sessionId") as string,
                    DedupeKey: info?.DedupeKey is { } key ? $"{kind}:{key}" : null,
                    DetailJson: detail));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Activity capture failed");
        }
    }
}
