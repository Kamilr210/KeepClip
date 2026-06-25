using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Run mode: default is the native desktop shell (a frameless WebView2 window with this
// server hosted in-process) — the C# port of main_desktop.py. Passing "server-only" (or
// KEEPCLIP_SERVER_ONLY=1) runs just the HTTP server, for API testing in a browser.
bool serverOnly = args.Contains("server-only")
    || Environment.GetEnvironmentVariable("KEEPCLIP_SERVER_ONLY") == "1";

// Single instance (desktop mode). Two copies are not a cosmetic problem: both run a
// replay buffer, and two Desktop Duplication consumers on one output evict each other
// in an endless restart loop — the ring never fills, hotkey saves fail, and each
// re-acquisition can kick a fullscreen game out of flip (looks like a forced alt-tab).
// The `using` keeps the mutex alive (and owned) for the whole app lifetime.
bool isFirstInstance = true;
using var singleInstance = serverOnly ? null : new Mutex(true, @"Local\KeepClip-SingleInstance", out isFirstInstance);
if (!isFirstInstance)
{
    DesktopShell.FocusExistingInstance();
    return;
}

DevLog.Install();   // real Console.Error tee in dev builds; a no-op in public releases

// Quiet the per-request "Request starting/finished" info logs (heartbeat + UI status
// polls fire every few seconds): useless in production (no console) and they would
// drown the dev log panel. Warnings/errors from these categories still come through.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);

// Loopback only, like uvicorn. Server-only uses a fixed dev port (8770, clear of the old
// Python app on 8765); the desktop shell grabs a free port, like pywebview's get_free_port.
string baseUrl = serverOnly
    ? (builder.Configuration["urls"] ?? "http://127.0.0.1:8770")
    : $"http://127.0.0.1:{FreeLoopbackPort()}";
builder.WebHost.UseUrls(baseUrl);

// Serialize exactly the keys we name (snake_case), like FastAPI/Pydantic. No
// camelCasing of object members or dictionary keys.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = null;
    o.SerializerOptions.DictionaryKeyPolicy = null;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// Data layer — stateless repositories (each opens its own short-lived SQLite connection).
builder.Services.AddSingleton<ClipRepository>();
builder.Services.AddSingleton<SegmentRepository>();
builder.Services.AddSingleton<FolderRepository>();
builder.Services.AddSingleton<StatsRepository>();

// Service layer — business logic on top of the repositories + infrastructure.
builder.Services.AddSingleton<ClipService>();

var app = builder.Build();

// One-time startup: ensure the DB + data dirs exist.
Db.InitDb();
Directory.CreateDirectory(Config.ThumbsDir);
Directory.CreateDirectory(Config.TmpDir);

// Static frontend (served under /static; index.html with cache-busting is in ConfigEndpoints).
var staticFiles = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Config.FrontendDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = staticFiles,
    RequestPath = "/static",
    ServeUnknownFileTypes = true,
});

// API endpoint groups — one file per area under Endpoints/.
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

// Exit after the frontend has been gone past the idle timeout (backstop in desktop mode).
StartIdleWatcher();

// Instant replay: resume the buffer if the user left it enabled, and make sure the
// capture ffmpeg never outlives us (ProcessExit also covers the idle-watcher exit).
ReplayService.ApplyConfig();
AppDomain.CurrentDomain.ProcessExit += (_, _) => ReplayService.Shutdown();

if (serverOnly)
{
    app.Run();   // blocks on the fixed dev URL until shutdown
    ReplayService.Shutdown();
    return;
}

// Desktop: start the server in the background, then open the native window on its own STA
// thread. When the window closes the message loop ends, so we stop the host and exit.
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

// Exit once the frontend has been gone (no heartbeat) past the idle timeout, matching the
// Python app. Guarded so an in-flight transcription is never interrupted.
static void StartIdleWatcher()
{
    var t = new Thread(() =>
    {
        while (true)
        {
            Thread.Sleep(15_000);
            if (TranscribeState.Snapshot().running) continue;
            if (Heartbeat.IdleSeconds > Heartbeat.IdleTimeoutSeconds) Environment.Exit(0);
        }
    })
    { IsBackground = true, Name = "KeepClip-Idle" };
    t.Start();
}
