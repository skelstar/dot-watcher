using DotWatcher.Server;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var logBuffer = new LogBuffer();
builder.Services.AddSingleton(logBuffer);
builder.Logging.AddProvider(new LogBufferProvider(logBuffer));

builder.Services.AddSingleton<BearerTokenAuth>();
builder.Services.AddSingleton<UserTokenAuth>();
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
