using DotWatcher.Server;

var builder = WebApplication.CreateBuilder(args);

var logBuffer = new LogBuffer();
builder.Services.AddSingleton(logBuffer);
builder.Logging.AddProvider(new LogBufferProvider(logBuffer));

builder.Services.AddSingleton(new BearerTokenAuth(builder.Configuration));

var dbPath = builder.Configuration.GetValue<string>("DbPath", "dotwatcher.db")!;
var store = new SessionStore(dbPath);
store.Initialize();
builder.Services.AddSingleton(store);

builder.Services.AddControllers();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.MapControllers();

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

app.Run();
