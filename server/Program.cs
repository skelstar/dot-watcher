using DotWatcher.Server;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Context;
using System.Diagnostics;
using System.Reflection;
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
    var connectionString = sp.GetRequiredService<IConfiguration>()["ConnectionString"]
        ?? throw new InvalidOperationException(
            "ConnectionString is not configured. Set it via appsettings or the ConnectionString environment variable.");
    var store = new SessionStore(connectionString);
    store.Initialize();
    return store;
});

builder.Services.AddControllers();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DotWatcher API",
        Version = "v1",
        Description = "REST API consumed by the DotWatcher web, iOS, and Android clients, plus " +
            "the bearer-token-authenticated admin/ops endpoints.",
    });

    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Either a user access token from /auth/login (most endpoints) or the " +
            "shared admin bearer token (admin/ops endpoints).",
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

// Environment isn't set via ASPNETCORE_ENVIRONMENT in deployed containers, so it's derived from the
// request host instead, matching the "main" -> dot-watcher-server / "staging" -> dot-watcher-server-staging
// deployment naming convention.
static string ResolveEnvironment(string host) =>
    host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.StartsWith("127.0.0.1")
        ? "Local"
        : host.Contains("staging", StringComparison.OrdinalIgnoreCase)
            ? "Staging"
            : "Production";

app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    var path = ctx.Request.Path.Value ?? "/";
    var method = ctx.Request.Method;
    var requestUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
    var environment = ResolveEnvironment(ctx.Request.Host.Host);

    var captureBody = !path.StartsWith("/auth") && !path.EndsWith("/recording");
    var originalBody = ctx.Response.Body;
    MemoryStream? capture = null;
    if (captureBody)
    {
        capture = new MemoryStream();
        ctx.Response.Body = capture;
    }

    // Pushed to the ambient LogContext (rather than only attached to the summary log below) so
    // every log statement emitted while handling this request - including ILogger calls from
    // controllers - carries the RequestUrl and Environment that produced them.
    using var _ = LogContext.PushProperty("RequestUrl", requestUrl);
    using var __ = LogContext.PushProperty("Environment", environment);

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

        if (ctx.Request.Headers.TryGetValue("X-Device-Id", out var deviceId) && !string.IsNullOrWhiteSpace(deviceId))
            log = log.ForContext("DeviceId", deviceId.ToString());

        if (ctx.Request.Headers.TryGetValue("X-Browser-Id", out var browserId) && !string.IsNullOrWhiteSpace(browserId))
            log = log.ForContext("BrowserId", browserId.ToString());

        if (ctx.Request.Headers.TryGetValue("X-Api-Version", out var apiVersion) && !string.IsNullOrWhiteSpace(apiVersion))
            log = log.ForContext("ApiVersion", apiVersion.ToString());

        if (ctx.Request.Headers.TryGetValue("X-Client-Id", out var clientId) && !string.IsNullOrWhiteSpace(clientId))
            log = log.ForContext("ClientId", clientId.ToString());


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
            var hasRunner = ctx.Items.TryGetValue("Log:Runner", out var logRunner);
            var hasCode = ctx.Items.TryGetValue("Log:Code", out var logCode);
            var message = hasRunner
                ? hasCode ? $"{method} {path} - {logRunner} - {logCode}" : $"{method} {path} - {logRunner}"
                : $"{method} {path}";
            if (status >= 500) log.Error(message);
            else if (status >= 400) log.Warning(message);
            else log.Information(message);

            // Write directly to the DI-scoped LogBuffer — avoids global Log.Logger isolation issues in tests
            var buffer = ctx.RequestServices.GetRequiredService<LogBuffer>();
            var level = status >= 500 ? "ERR" : status >= 400 ? "WRN" : "INF";
            var contextSuffix = "";
            if (ctx.Items.TryGetValue("Log:Session", out var logSession) && hasRunner)
                contextSuffix = $" [{logSession}] {logRunner}";
            buffer.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss} {level}] {message}{contextSuffix}");
        }
    }
});

// Rejects requests from clients whose X-Api-Version is below the configured floor. A missing/
// unparseable header is always allowed — every currently-installed client predates this header,
// and dev/ops tooling (Bruno, the simulator, live-run.sh) never sends it. Admin-bearer-authenticated
// traffic bypasses the check entirely since it's internal tooling, not an app-store-released client.
var minimumApiVersion = app.Configuration.GetValue<int>("MinimumApiVersion", 1);
app.Use(async (ctx, next) =>
{
    var bearerAuth = ctx.RequestServices.GetRequiredService<BearerTokenAuth>();
    if (!bearerAuth.IsAuthorized(ctx.Request)
        && ctx.Request.Headers.TryGetValue("X-Api-Version", out var versionHeader)
        && int.TryParse(versionHeader, out var clientVersion)
        && clientVersion < minimumApiVersion)
    {
        ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await ctx.Response.WriteAsJsonAsync(new { error = "Please update the app to continue." });
        return;
    }

    await next();
});

app.MapControllers();

_ = app.Services.GetRequiredService<BearerTokenAuth>();
_ = app.Services.GetRequiredService<UserTokenAuth>();
var store = app.Services.GetRequiredService<SessionStore>();

var demoSessionConfig = app.Configuration.GetSection("DemoSession");
store.EnsureDemoSession(
    demoSessionConfig["InviteCode"] ?? "ABC123",
    demoSessionConfig["DisplayName"] ?? "DW",
    demoSessionConfig.GetValue<double>("AnchorLatitude", -41.2865),
    demoSessionConfig.GetValue<double>("AnchorLongitude", 174.7762),
    demoSessionConfig.GetValue<double>("LoopRadiusMeters", 150),
    demoSessionConfig.GetValue<double>("LoopPeriodSeconds", 360));

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
