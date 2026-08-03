using System.Text;

namespace KeepClip.Endpoints;

public static class ConfigEndpoints
{
#if KEEPCLIP_DEV
    private const bool DevBuild = true;   // Steruje widocznością narzędzia deweloperskiego.
#else
    private const bool DevBuild = false;
#endif

    private static string AccentThemeOrDefault()
        => Settings.GetString("accent_theme") == "green" ? "green" : "purple";

    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/", () =>
        {
            var idx = Path.Combine(Config.FrontendDir, "index.html");
            if (!File.Exists(idx))
                return Results.Content("<h1>frontend missing</h1>", "text/html", Encoding.UTF8, 500);
            var html = File.ReadAllText(idx, Encoding.UTF8);
            var accentTheme = AccentThemeOrDefault();
            html = html.Replace("<html lang=\"pl\">", $"<html lang=\"pl\" data-accent-theme=\"{accentTheme}\">");
            if (accentTheme == "green")
                html = html.Replace("/icons/favicon.svg", "/icons/favicon-green.svg");
            // Czas modyfikacji wymusza pobranie nowej wersji CSS/JS przez przeglądarkę.
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
            var currentVersion = UpdateService.Current;
        // Starsze instalacje mogą nie mieć pliku ustawień, mimo że baza zawiera klipy.
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
                accent_theme = AccentThemeOrDefault(),
                language = Settings.GetLanguage(),
                version = $"{currentVersion.Major}.{currentVersion.Minor}.{Math.Max(currentVersion.Build, 0)}",
            });
        });

        app.MapPost("/api/config", (ConfigPayload body) =>
        {
            Heartbeat.Touch();
            if (TranscribeState.Snapshot().running)
                return Api.Detail(409, Strings.Get("config.transcribeBusy"), "configTranscribeBusy");
            var newRoot = body.clips_root?.Trim().Trim('"').Trim('\'') ?? "";
            if (string.IsNullOrEmpty(newRoot))
                return Api.Detail(400, Strings.Get("config.pathRequired"), "configPathRequired");
            if (!Path.Exists(newRoot))
                return Api.Detail(400, Strings.Get("config.pathMissing", newRoot));
            if (!Directory.Exists(newRoot))
                return Api.Detail(400, Strings.Get("config.notAFolder", newRoot));
            Settings.SetClipsRoot(newRoot);
        var scan = Scanner.Scan();              // Skanuje od razu, aby pokazać zawartość.
            ThumbnailWorker.Ensure();
            DevLog.Add($"Zmieniono folder klipów na: {newRoot} (skan: +{scan.GetValueOrDefault("added")} / −{scan.GetValueOrDefault("removed")})");
            return Results.Json(new Dictionary<string, object?>
            {
                ["ok"] = true, ["clips_root"] = newRoot, ["scan"] = scan,
            });
        });

        app.MapPost("/api/accent-theme", (AccentThemePayload body) =>
        {
            Heartbeat.Touch();
            var theme = body.theme?.Trim().ToLowerInvariant();
            if (theme is not ("purple" or "green"))
                return Api.Detail(400, Strings.Get("config.badTheme"), "configBadTheme");
            Settings.SetString("accent_theme", theme);
            return Results.Json(new { ok = true, theme });
        });

        // Interfejs zgłasza tu swój język, aby transkrypcja rozpoznawała mowę w tym samym.
        app.MapPost("/api/language", (LanguagePayload body) =>
        {
            Heartbeat.Touch();
            var language = body.language?.Trim().ToLowerInvariant();
            if (language is null || !Config.UiLanguages.Contains(language))
                return Api.Detail(400, Strings.Get("config.badLanguage"), "configBadLanguage");
            if (language != Settings.GetLanguage())
                DevLog.Add($"Zmieniono język na: {language} (dotyczy też transkrypcji)");
            Settings.SetLanguage(language);
            return Results.Json(new { ok = true, language });
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

        // Odpowiedź musi zostać wysłana przed zamknięciem procesu; aktywna transkrypcja
        // blokuje wyjście, aby nie przerwać zapisu w połowie.
        app.MapPost("/api/shutdown", () =>
        {
            if (TranscribeState.Snapshot().running)
                return Results.Json(new { ok = false, reason = "transcription_running" });
            _ = Task.Run(async () => { await Task.Delay(500); Environment.Exit(0); });
            return Results.Json(new { ok = true });
        });

#if KEEPCLIP_DEV
    // Ten punkt końcowy nie jest kompilowany w wydaniach publicznych.
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
record AccentThemePayload(string? theme);
record LanguagePayload(string? language);
