using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotDots.Service;

public sealed record RouteResult(IReadOnlyList<LatLon> Shape, double LengthMeters);

/// <summary>Finds a walking route between two points. Null when there isn't one (sea, no path nearby, server trouble).</summary>
public interface IRouter
{
    /// <param name="startHeading">The way the bot-dot is already facing, so the route doesn't open with a U-turn.</param>
    Task<RouteResult?> RouteAsync(LatLon from, LatLon to, double? startHeading, CancellationToken cancellationToken);
}

/// <summary>Valhalla's pedestrian costing, with no preference for trails (see bot-dots/spike).</summary>
public sealed class ValhallaRouter(HttpClient http, IOptions<BotDotsOptions> options, ILogger<ValhallaRouter> logger) : IRouter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public async Task<RouteResult?> RouteAsync(LatLon from, LatLon to, double? startHeading, CancellationToken cancellationToken)
    {
        var start = new Dictionary<string, object> { ["lat"] = from.Lat, ["lon"] = from.Lon };
        if (startHeading is { } heading)
        {
            start["heading"] = (int)Math.Round(Geo.NormalizeDegrees(heading));
            start["heading_tolerance"] = 60;
        }

        var body = new
        {
            locations = new object[] { start, new Dictionary<string, object> { ["lat"] = to.Lat, ["lon"] = to.Lon } },
            costing = "pedestrian",
            directions_type = "none",
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wait = TimeSpan.FromMilliseconds(options.Value.ValhallaMinIntervalMs) - (DateTimeOffset.UtcNow - _lastRequest);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
            _lastRequest = DateTimeOffset.UtcNow;

            using var response = await http.PostAsJsonAsync("/route", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null; // Valhalla answers 400 when there is no route (e.g. a point in the sea).

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var trip = document.RootElement.GetProperty("trip");
            var shape = Polyline.Decode6(trip.GetProperty("legs")[0].GetProperty("shape").GetString()!);
            var meters = trip.GetProperty("summary").GetProperty("length").GetDouble() * 1000;
            return shape.Count < 2 ? null : new RouteResult(shape, meters);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Valhalla route failed");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
