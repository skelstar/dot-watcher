using DotWatcher.Server;
using Serilog;
using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var logBuffer = new LogBuffer();
builder.Services.AddSingleton(logBuffer);

builder.Host.UseSerilog((ctx, services, config) =>
{
    var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL") ?? "https://seq.skelstar.io";
    var version = Environment.GetEnvironmentVariable("APP_VERSION")
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown";
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "dot-watcher")
        .Enrich.WithProperty("Version", version)
        .WriteTo.Console()
        .WriteTo.Seq(seqUrl);
});

builder.Services.AddSingleton<BearerTokenAuth>();
builder.Services.AddSingleton<UserTokenAuth>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuthAttemptLimiter>();
builder.Services.AddSingleton(sp =>
{
    var dbPath = sp.GetRequiredService<IConfiguration>().GetValue<string>("DbPath", "dotwatcher.db")!;
    var store = new SessionStore(dbPath);
    store.Initialize();
    return store;
});

builder.Services.AddControllers();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    var path = ctx.Request.Path.Value ?? "/";
    var method = ctx.Request.Method;

    var captureBody = !path.StartsWith("/auth") && !path.EndsWith("/recording");
    var originalBody = ctx.Response.Body;
    MemoryStream? capture = null;
    if (captureBody)
    {
        capture = new MemoryStream();
        ctx.Response.Body = capture;
    }

    try { await next(); }
    finally
    {
        sw.Stop();

        string? responseBody = null;
        if (capture != null)
        {
            capture.Seek(0, SeekOrigin.Begin);
            var raw = await new StreamReader(capture).ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(raw))
                responseBody = raw.Length > 2048 ? raw[..2048] + "…" : raw;
            capture.Seek(0, SeekOrigin.Begin);
            await capture.CopyToAsync(originalBody);
            ctx.Response.Body = originalBody;
        }

        var status = ctx.Response.StatusCode;
        var log = Log.ForContext("RequestMethod", method)
                     .ForContext("RequestPath", path)
                     .ForContext("StatusCode", status)
                     .ForContext("Elapsed", sw.Elapsed.TotalMilliseconds);

        if (ctx.GetRouteValue("sessionId") is string sessionId)
            log = log.ForContext("SessionId", sessionId);

        if (ctx.Request.Headers.TryGetValue("X-Device-Name", out var deviceName) && !string.IsNullOrWhiteSpace(deviceName))
            log = log.ForContext("DeviceName", deviceName.ToString());

        var userAuth = ctx.RequestServices.GetRequiredService<UserTokenAuth>();
        if (userAuth.TryAuthenticate(ctx.Request, out var user))
        {
            log = log.ForContext("UserId", user.UserId);
            log = log.ForContext("Username", user.Username);
        }

        foreach (var (key, value) in ctx.Items)
            if (key is string k && k.StartsWith("Log:") && value is not null)
                log = log.ForContext(k[4..], value);

        if (responseBody != null)
            log = log.ForContext("ResponseBody", responseBody);

        if (path != "/log") // skip noisy debug-panel polling
        {
            var message = $"{method} {path}";
            if (status >= 500) log.Error(message);
            else if (status >= 400) log.Warning(message);
            else log.Information(message);

            // Write directly to the DI-scoped LogBuffer — avoids global Log.Logger isolation issues in tests
            var buffer = ctx.RequestServices.GetRequiredService<LogBuffer>();
            var level = status >= 500 ? "ERR" : status >= 400 ? "WRN" : "INF";
            var contextSuffix = "";
            if (ctx.Items.TryGetValue("Log:Session", out var logSession) &&
                ctx.Items.TryGetValue("Log:Runner", out var logRunner))
                contextSuffix = $" [{logSession}] {logRunner}";
            buffer.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss} {level}] {message}{contextSuffix}");
        }
    }
});
app.MapControllers();

_ = app.Services.GetRequiredService<BearerTokenAuth>();
_ = app.Services.GetRequiredService<UserTokenAuth>();
var store = app.Services.GetRequiredService<SessionStore>();

// Migrate any existing NDJSON recordings into SQLite
var recordingsPath = app.Configuration.GetValue<string>("RecordingsPath", "recordings")!;
if (Directory.Exists(recordingsPath))
{
    foreach (var file in Directory.GetFiles(recordingsPath, "*.ndjson"))
    {
        var code = Path.GetFileNameWithoutExtension(file)!.ToUpperInvariant();
        if (!store.HasRecording(code))
        {
            try
            {
                store.SaveRecording(code, File.ReadAllText(file));
                app.Logger.LogInformation("Migrated recording {Session} from NDJSON", code);
            }
            catch (Exception ex) when (ex is JsonException or LocationUpdateValidationException)
            {
                app.Logger.LogWarning(ex, "Skipped invalid legacy NDJSON recording {Session}", code);
            }
        }
    }
}

app.Run();

public partial class Program;
