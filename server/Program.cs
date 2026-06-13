using DotWatcher.Server;

var builder = WebApplication.CreateBuilder(args);

var positionHistoryCount = builder.Configuration.GetValue<int>("PositionHistoryCount", 3);
builder.Services.AddSingleton(new SessionStore(positionHistoryCount));
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

var bearerToken = app.Configuration["BearerToken"]
    ?? throw new InvalidOperationException(
        "BearerToken is not configured. Set it via appsettings or the BearerToken environment variable.");

bool IsAuthorized(HttpRequest request) =>
    request.Headers.TryGetValue("Authorization", out var auth) &&
    auth.ToString() == $"Bearer {bearerToken}";

// Receive a position update from a phone app
app.MapPost("/location", (LocationUpdate update, SessionStore store, HttpRequest request, ILogger<Program> logger) =>
{
    if (!IsAuthorized(request))
        return Results.Unauthorized();

    store.AddPosition(update);
    logger.LogInformation("[{Session}] {Runner} → {Lat:F6}, {Lon:F6}  heading={Heading}  t={Timestamp:HH:mm:ss}",
        update.SessionCode, update.RunnerName,
        update.Latitude, update.Longitude,
        update.Heading.HasValue ? $"{update.Heading:F1}°" : "n/a",
        update.Timestamp);
    return Results.Ok();
});

// Return the latest position for every runner in a session
app.MapGet("/locations/{sessionCode}", (string sessionCode, SessionStore store) =>
    Results.Ok(store.GetLatestPositions(sessionCode)));

// Clear all history for a session (use between runs)
app.MapDelete("/sessions/{sessionCode}", (string sessionCode, SessionStore store, HttpRequest request) =>
{
    if (!IsAuthorized(request))
        return Results.Unauthorized();

    store.ClearSession(sessionCode);
    return Results.NoContent();
});

app.Run();
