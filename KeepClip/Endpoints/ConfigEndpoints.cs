using System.Text;

namespace KeepClip.Endpoints;

/// <summary>Frontend hosting + liveness/config/scan/shutdown, and the dev-only log feed.</summary>
public static class ConfigEndpoints
{
#if KEEPCLIP_DEV
    private const bool DevBuild = true;   // gates the developer-only "Logi aplikacji" tool in the UI
#else
    private const bool DevBuild = false;
#endif

    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/", () =>
        {
            var idx = Path.Combine(Config.FrontendDir, "index.html");
            if (!File.Exists(idx))
                return Results.Content("<h1>frontend missing</h1>", "text/html", Encoding.UTF8, 500);
            var html = File.ReadAllText(idx, Encoding.UTF8);
            // Cache-bust static assets by mtime so an updated CSS/JS reaches the browser.
            foreach (var name in new[] { "styles.css", "app.js" })
            {
                var f = Path.Combine(Config.FrontendDir, name);
                if (File.Exists(f))
                {
                    var v = ((DateTimeOffset)File.GetLastWriteTimeUtc(f)).ToUnixTimeSeconds();
                    html = html.Replace($"/static/{name}", $"/static/{name}?v={v}");
                }
            }
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/api/ping", () => Results.Json(new { ok = true }));

        app.MapGet("/api/config", (ClipRepository clips) =>
        {
            Heartbeat.Touch();
            var root = Settings.GetClipsRoot();
            var configured = Settings.IsConfigured();
            // Backward compat: pre-existing installs already have clips registered;
            // treat them as configured even without an explicit settings.json.
            if (!configured && clips.Count() > 0)
            {
                Settings.SetClipsRoot(root);
                configured = true;
            }
            return Results.Json(new
            {
                clips_root = root,
                clips_root_exists = Directory.Exists(root),
                configured,
                dev = DevBuild,
            });
        });

        app.MapPost("/api/config", (ConfigPayload body) =>
        {
            Heartbeat.Touch();
            if (TranscribeState.Snapshot().running)
                return Api.Detail(409, "Nie można zmieniać folderu w trakcie transkrypcji.");
            var newRoot = body.clips_root?.Trim().Trim('"').Trim('\'') ?? "";
            if (string.IsNullOrEmpty(newRoot))
                return Api.Detail(400, "Ścieżka folderu jest wymagana.");
            if (!Path.Exists(newRoot))
                return Api.Detail(400, $"Folder nie istnieje: {newRoot}");
            if (!Directory.Exists(newRoot))
                return Api.Detail(400, $"To nie jest folder: {newRoot}");
            Settings.SetClipsRoot(newRoot);
            var scan = Scanner.Scan();              // scan immediately so contents show up
            ThumbnailWorker.Ensure();
            DevLog.Add($"Zmieniono folder klipów na: {newRoot} (skan: +{scan.GetValueOrDefault("added")} / −{scan.GetValueOrDefault("removed")})");
            return Results.Json(new Dictionary<string, object?>
            {
                ["ok"] = true, ["clips_root"] = newRoot, ["scan"] = scan,
            });
        });

        app.MapPost("/api/heartbeat", () => { Heartbeat.Touch(); return Results.Json(new { ok = true }); });

        app.MapPost("/api/scan", () =>
        {
            Heartbeat.Touch();
            var result = Scanner.Scan();
            ThumbnailWorker.Ensure();
            DevLog.Add($"Skan folderu: znaleziono {result.GetValueOrDefault("found")}, dodano {result.GetValueOrDefault("added")}, usunięto {result.GetValueOrDefault("removed")}");
            return Results.Json(result);
        });

        // Graceful process exit for the desktop shell's window-close. Refused mid-run so a
        // transcription is never killed halfway. Deferred so the HTTP response flushes first.
        app.MapPost("/api/shutdown", () =>
        {
            if (TranscribeState.Snapshot().running)
                return Results.Json(new { ok = false, reason = "transcription_running" });
            _ = Task.Run(async () => { await Task.Delay(500); Environment.Exit(0); });
            return Results.Json(new { ok = true });
        });

#if KEEPCLIP_DEV
        // Developer log viewer (compiled out of public releases).
        app.MapGet("/api/logs", (long? since) =>
        {
            var (seq, lines) = DevLog.GetSince(since ?? 0);
            return Results.Json(new { seq, lines });
        });
        app.MapPost("/api/logs/clear", () => { DevLog.Clear(); return Results.Json(new { ok = true }); });
#endif
    }
}

record ConfigPayload(string clips_root);
