using DotWatcher.Server;

var builder = WebApplication.CreateBuilder(args);

var logBuffer = new LogBuffer();
builder.Services.AddSingleton(logBuffer);
builder.Logging.AddProvider(new LogBufferProvider(logBuffer));

var dbPath = builder.Configuration.GetValue<string>("DbPath", "dotwatcher.db")!;
var store = new SessionStore(dbPath);
store.Initialize();
builder.Services.AddSingleton(store);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

// Migrate any existing NDJSON recordings into SQLite
var recordingsPath = app.Configuration.GetValue<string>("RecordingsPath", "recordings")!;
if (Directory.Exists(recordingsPath))
{
    foreach (var file in Directory.GetFiles(recordingsPath, "*.ndjson"))
    {
        var code = Path.GetFileNameWithoutExtension(file)!.ToUpperInvariant();
        if (!store.HasRecording(code))
        {
            store.SaveRecording(code, File.ReadAllText(file));
            app.Logger.LogInformation("Migrated recording {Session} from NDJSON", code);
        }
    }
}

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
    var participants = store.GetParticipants(update.SessionCode);
    return Results.Ok(new { participants });
});

// Return the latest position for every runner in a session
app.MapGet("/locations/{sessionCode}", (string sessionCode, SessionStore store) =>
    Results.Ok(store.GetLatestPositions(sessionCode)));

// List all recorded sessions
app.MapGet("/sessions", (SessionStore store) =>
    Results.Ok(store.GetRecordedSessions()));

// Upload an NDJSON recording for a session (replaces any existing data for that session)
app.MapPost("/sessions/{sessionCode}/recording", async (string sessionCode, SessionStore store, HttpRequest request, ILogger<Program> logger) =>
{
    if (!IsAuthorized(request))
        return Results.Unauthorized();

    using var reader = new StreamReader(request.Body);
    var content = await reader.ReadToEndAsync();
    var upper = sessionCode.ToUpperInvariant();
    store.SaveRecording(upper, content);
    logger.LogInformation("Uploaded recording for {Session} ({Bytes} bytes)", upper, content.Length);
    return Results.Ok(new { sessionCode = upper });
});

// Download the full NDJSON recording for a session
app.MapGet("/sessions/{sessionCode}/recording", (string sessionCode, SessionStore store) =>
{
    var upper = sessionCode.ToUpperInvariant();
    if (!store.HasRecording(upper)) return Results.NotFound();
    var ndjson = store.GetRecordingAsNdjson(upper);
    return Results.Text(ndjson, "application/x-ndjson");
});

// Delete the recording for a session
app.MapDelete("/sessions/{sessionCode}/recording", (string sessionCode, SessionStore store, HttpRequest request, ILogger<Program> logger) =>
{
    if (!IsAuthorized(request))
        return Results.Unauthorized();

    var upper = sessionCode.ToUpperInvariant();
    if (!store.DeleteRecording(upper)) return Results.NotFound();
    logger.LogInformation("Deleted recording for {Session}", upper);
    return Results.NoContent();
});

// Clear live tracking for a session (use between runs)
app.MapDelete("/sessions/{sessionCode}", (string sessionCode, SessionStore store, HttpRequest request) =>
{
    if (!IsAuthorized(request))
        return Results.Unauthorized();

    store.ClearSession(sessionCode);
    return Results.NoContent();
});

// Debug dashboard log feed
app.MapGet("/log", (LogBuffer log) => Results.Ok(log.GetAll()));

app.Run();
