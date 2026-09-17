using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

bool serverOnly = args.Contains("server-only")
    || Environment.GetEnvironmentVariable("KEEPCLIP_SERVER_ONLY") == "1";

bool isFirstInstance = true;
using var singleInstance = serverOnly ? null : new Mutex(true, @"Local\KeepClip-SingleInstance", out isFirstInstance);
if (!isFirstInstance)
{
    DesktopShell.FocusExistingInstance();
    return;
}

DevLog.Install();

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);

string baseUrl = serverOnly
    ? (builder.Configuration["urls"] ?? "http://127.0.0.1:8770")
    : $"http://127.0.0.1:{FreeLoopbackPort()}";
builder.WebHost.UseUrls(baseUrl);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = null;
    o.SerializerOptions.DictionaryKeyPolicy = null;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

builder.Services.AddSingleton<ClipRepository>();
builder.Services.AddSingleton<SegmentRepository>();
builder.Services.AddSingleton<FolderRepository>();
builder.Services.AddSingleton<StatsRepository>();

builder.Services.AddSingleton<PlaybackProxyService>();
builder.Services.AddSingleton<ClipService>();

var app = builder.Build();

Db.InitDb();
Directory.CreateDirectory(Config.ThumbsDir);
Directory.CreateDirectory(Config.TmpDir);
Directory.CreateDirectory(Config.PlaybackDir);

var staticFiles = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Config.FrontendDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = staticFiles,
    RequestPath = "/static",
    ServeUnknownFileTypes = true,
});

app.MapConfigEndpoints();
app.MapStatsEndpoints();
app.MapClipEndpoints();
app.MapSegmentEndpoints();
app.MapFolderEndpoints();
app.MapSearchEndpoints();
app.MapTranscribeEndpoints();
app.MapReplayEndpoints();
app.MapCloudEndpoints();
app.MapMediaEndpoints();
app.MapUpdateEndpoints();
app.MapOnboardingEndpoints();

StartIdleWatcher();

ReplayService.ApplyConfig();
AppDomain.CurrentDomain.ProcessExit += (_, _) => ReplayService.Shutdown();

if (serverOnly)
{
    app.Run();
    ReplayService.Shutdown();
    return;
}

await app.StartAsync();
var ui = new Thread(() => DesktopShell.Run(baseUrl)) { Name = "KeepClip-UI" };
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();
ReplayService.Shutdown();
await app.StopAsync();

static int FreeLoopbackPort()
{
    using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    l.Start();
    int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return p;
}

static void StartIdleWatcher()
{
    var t = new Thread(() =>
    {
        while (true)
        {
            Thread.Sleep(15_000);
            if (TranscribeState.Snapshot().running) continue;
            if (ReplayService.Enabled && ReplayService.BackgroundEnabled) continue;
            if (Heartbeat.IdleSeconds > Heartbeat.IdleTimeoutSeconds) Environment.Exit(0);
        }
    })
    { IsBackground = true, Name = "KeepClip-Idle" };
    t.Start();
}
