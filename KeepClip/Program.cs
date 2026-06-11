using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KeepClip;

var builder = WebApplication.CreateBuilder(args);

// Run mode: default is the native desktop shell (a frameless WebView2 window with this
// server hosted in-process) — the C# port of main_desktop.py. Passing "server-only" (or
// KEEPCLIP_SERVER_ONLY=1) runs just the HTTP server, for API testing in a browser.
bool serverOnly = args.Contains("server-only")
    || Environment.GetEnvironmentVariable("KEEPCLIP_SERVER_ONLY") == "1";

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

var app = builder.Build();

// One-time startup: ensure the DB + data dirs exist.
Db.InitDb();
Directory.CreateDirectory(Config.ThumbsDir);
Directory.CreateDirectory(Config.TmpDir);

// ---------- helpers ----------

// FastAPI's HTTPException serializes to {"detail": "..."} with the given status.
static IResult Detail(int code, string msg)
    => Results.Json(new Dictionary<string, object?> { ["detail"] = msg }, statusCode: code);

static string VideoContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".mp4" => "video/mp4",
    ".webm" => "video/webm",
    ".mov" => "video/quicktime",
    ".mkv" => "video/x-matroska",
    ".avi" => "video/x-msvideo",
    _ => "video/mp4",
};

// JSON options for the hand-written SSE frames (Results.Json uses the DI-configured
// options; the manual Response.WriteAsync path needs its own). Keep keys verbatim.
var sseJson = new JsonSerializerOptions { PropertyNamingPolicy = null, DictionaryKeyPolicy = null };

// Turn 'nie no co ty robisz' into '"nie"* AND "no"* AND ...' (AND'ed prefix tokens)
// so substring/inflection matches work with the diacritic-insensitive tokenizer.
var ftsSafe = new Regex(@"[\wÀ-ſ]+", RegexOptions.Compiled);
string ToFtsQuery(string q)
{
    var tokens = ftsSafe.Matches(q.ToLowerInvariant()).Select(m => m.Value).ToList();
    if (tokens.Count == 0) return "";
    return string.Join(" AND ", tokens.Select(t => $"\"{t}\"*"));
}

// ---------- static frontend ----------
var staticFiles = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Config.FrontendDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = staticFiles,
    RequestPath = "/static",
    ServeUnknownFileTypes = true,
});

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

// ---------- liveness / config ----------
app.MapGet("/api/ping", () => Results.Json(new { ok = true }));

app.MapGet("/api/config", () =>
{
    Heartbeat.Touch();
    var root = Settings.GetClipsRoot();
    var configured = Settings.IsConfigured();
    // Backward compat: pre-existing installs already have clips registered;
    // treat them as configured even without an explicit settings.json.
    if (!configured)
    {
        using var con = Db.Open();
        if (con.ScalarLong("SELECT COUNT(*) FROM clips") > 0)
        {
            Settings.SetClipsRoot(root);
            configured = true;
        }
    }
    return Results.Json(new
    {
        clips_root = root,
        clips_root_exists = Directory.Exists(root),
        configured,
    });
});

app.MapPost("/api/config", (ConfigPayload body) =>
{
    Heartbeat.Touch();
    if (TranscribeState.Snapshot().running)
        return Detail(409, "Nie można zmieniać folderu w trakcie transkrypcji.");
    var newRoot = body.clips_root?.Trim().Trim('"').Trim('\'') ?? "";
    if (string.IsNullOrEmpty(newRoot))
        return Detail(400, "Ścieżka folderu jest wymagana.");
    if (!Path.Exists(newRoot))
        return Detail(400, $"Folder nie istnieje: {newRoot}");
    if (!Directory.Exists(newRoot))
        return Detail(400, $"To nie jest folder: {newRoot}");
    Settings.SetClipsRoot(newRoot);
    var scan = Scanner.Scan();              // scan immediately so contents show up
    ThumbnailWorker.Ensure();
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
    return Results.Json(result);
});

// ---------- instant replay (rolling capture buffer + hotkey save) ----------
app.MapGet("/api/replay/status", () =>
{
    Heartbeat.Touch();
    return Results.Json(ReplayService.Status());
});

app.MapPost("/api/replay/config", (ReplayConfigPayload body) =>
{
    Heartbeat.Touch();
    if (body.duration_s is < 15 or > 600)
        return Detail(400, "Długość powtórki musi być w zakresie 15–600 sekund.");
    if (body.fps is not null && body.fps is not (30 or 60))
        return Detail(400, "Obsługiwane wartości FPS: 30 lub 60.");
    if (body.quality is not null && body.quality is not ("low" or "medium" or "high"))
        return Detail(400, "Jakość musi być jedną z: low, medium, high.");
    if (body.hotkey is not null && !HotkeyManager.TryParse(body.hotkey, out _, out _))
        return Detail(400, "Nieprawidłowy skrót — użyj modyfikatora i klawisza, np. Alt+F10.");

    if (body.enabled is not null) Settings.SetString("replay_enabled", body.enabled.Value ? "1" : "0");
    if (body.duration_s is not null) Settings.SetString("replay_duration_s", body.duration_s.Value.ToString());
    if (body.fps is not null) Settings.SetString("replay_fps", body.fps.Value.ToString());
    if (body.quality is not null) Settings.SetString("replay_quality", body.quality);
    if (body.hotkey is not null) Settings.SetString("replay_hotkey", body.hotkey);
    if (body.mic is not null) Settings.SetString("replay_mic", body.mic.Value ? "1" : "0");

    ReplayService.ApplyConfig();   // start/stop/restart the buffer to match
    HotkeyManager.Refresh();       // re-register the (possibly new) combo
    return Results.Json(ReplayService.Status());
});

app.MapPost("/api/replay/save", async () =>
{
    Heartbeat.Touch();
    try { return Results.Json(await ReplayService.SaveAsync("ui")); }
    catch (InvalidOperationException ex) { return Detail(409, ex.Message); }
    catch (Exception ex) { return Detail(500, ex.Message); }
});

// ---------- transcription ----------
app.MapGet("/api/transcribe/status", () => Results.Json(TranscribeState.Snapshot()));

app.MapPost("/api/transcribe/start", (bool? force) =>
{
    Heartbeat.Touch();
    bool f = force ?? false;
    if (!TranscribeWorker.Start(f, out int total))
        return Results.Json(new { started = false, reason = "already_running" });
    return Results.Json(new Dictionary<string, object?> { ["started"] = true, ["force"] = f, ["total"] = total });
});

app.MapPost("/api/transcribe/cancel", () =>
{
    Heartbeat.Touch();
    lock (TranscribeState.Lock)
    {
        if (!TranscribeState.Running)
            return Results.Json(new { cancelled = false, reason = "not_running" });
        TranscribeState.Cancel = true;   // the worker checks this between clips
    }
    return Results.Json(new { cancelled = true });
});

// Server-Sent Events progress feed — a port of /api/transcribe/stream. Emits a frame
// whenever the snapshot changes, then one final frame once a run has finished.
app.MapGet("/api/transcribe/stream", async (HttpContext ctx) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";   // disable any proxy buffering

    var ct = ctx.RequestAborted;
    string? last = null;
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var snap = TranscribeState.Snapshot();
            var payload = JsonSerializer.Serialize(snap, sseJson);
            if (payload != last)
            {
                await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                last = payload;
            }
            // Terminal state already emitted above (running flipped / finished_at set
            // both change the payload) → close the stream.
            if (!snap.running && snap.finished_at is not null) break;
            await Task.Delay(500, ct);
        }
    }
    catch (OperationCanceledException) { /* client disconnected — normal */ }
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

// ---------- stats ----------
app.MapGet("/api/stats", () =>
{
    using var con = Db.Open();
    var clips = con.ScalarLong("SELECT COUNT(*) FROM clips");
    var done = con.ScalarLong("SELECT COUNT(*) FROM clips WHERE transcribed_at IS NOT NULL");
    var segs = con.ScalarLong("SELECT COUNT(*) FROM segments");
    var totalBytes = con.ScalarLong("SELECT COALESCE(SUM(size_bytes), 0) FROM clips");
    var localBytes = con.ScalarLong(
        "SELECT COALESCE(SUM(size_bytes), 0) FROM clips WHERE storage IS NULL OR storage != 'cloud'");
    var cloudBytes = con.ScalarLong("SELECT COALESCE(SUM(size_bytes), 0) FROM clips WHERE storage = 'cloud'");
    var favorites = con.ScalarLong("SELECT COUNT(*) FROM clips WHERE favorite = 1");
    var games = con.Query(
        @"SELECT game, COUNT(*) AS clips,
                 SUM(CASE WHEN transcribed_at IS NOT NULL THEN 1 ELSE 0 END) AS done
          FROM clips GROUP BY game ORDER BY game");

    long? diskTotal = null, diskFree = null;
    try
    {
        var root = Settings.GetClipsRoot();
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            diskTotal = di.TotalSize;
            diskFree = di.AvailableFreeSpace;
        }
    }
    catch { /* disk info best-effort */ }

    return Results.Json(new Dictionary<string, object?>
    {
        ["clips"] = clips,
        ["transcribed"] = done,
        ["segments"] = segs,
        ["total_bytes"] = totalBytes,
        ["local_bytes"] = localBytes,
        ["cloud_bytes"] = cloudBytes,
        ["disk_total"] = diskTotal,
        ["disk_free"] = diskFree,
        ["favorites"] = favorites,
        ["games"] = games,
    });
});

// ---------- clips ----------
var clipSorts = new Dictionary<string, string>
{
    ["newest"] = "mtime DESC",
    ["oldest"] = "mtime ASC",
    ["largest"] = "size_bytes DESC",
    ["smallest"] = "size_bytes ASC",
};

app.MapGet("/api/clips", (string? game, string? sort, int? limit, int? favorite) =>
{
    var order = clipSorts.GetValueOrDefault(sort ?? "newest", clipSorts["newest"]);
    var sql = "SELECT id, game, filename, duration, size_bytes, mtime, has_thumb, " +
              "transcribed_at, favorite, storage FROM clips";
    var where = new List<string>();
    var ps = new List<(string, object?)>();
    if (!string.IsNullOrEmpty(game)) { where.Add("game = $game"); ps.Add(("$game", game)); }
    if (favorite is not null && favorite != 0) where.Add("favorite = 1");
    if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
    sql += $" ORDER BY {order} LIMIT $limit";
    ps.Add(("$limit", limit ?? 200));
    using var con = Db.Open();
    return Results.Json(con.Query(sql, ps.ToArray()));
});

app.MapPost("/api/clips/{clipId:long}/favorite", (long clipId) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    var row = con.QueryOne("SELECT favorite FROM clips WHERE id = $id", ("$id", clipId));
    if (row is null) return Detail(404, "Klip nie istnieje.");
    var newState = Convert.ToInt64(row["favorite"]) != 0 ? 0 : 1;
    con.Exec("UPDATE clips SET favorite = $f WHERE id = $id", ("$f", newState), ("$id", clipId));
    return Results.Json(new { ok = true, id = clipId, favorite = newState != 0 });
});

// ---------- search ----------
var searchSorts = new Dictionary<string, string>
{
    ["relevance"] = "score ASC",
    ["newest"] = "c.mtime DESC, score ASC",
    ["oldest"] = "c.mtime ASC, score ASC",
    ["largest"] = "c.size_bytes DESC, score ASC",
    ["smallest"] = "c.size_bytes ASC, score ASC",
};

app.MapGet("/api/search", (string q, string? sort, int? limit) =>
{
    if (string.IsNullOrEmpty(q)) return Detail(422, "q is required");
    var ftsQ = ToFtsQuery(q);
    if (string.IsNullOrEmpty(ftsQ))
        return Results.Json(new Dictionary<string, object?> { ["query"] = q, ["results"] = Array.Empty<object>() });
    var order = searchSorts.GetValueOrDefault(sort ?? "relevance", searchSorts["relevance"]);
    using var con = Db.Open();
    var rows = con.Query(
        $@"SELECT s.id AS segment_id, s.start_s, s.end_s, s.text,
                  c.id AS clip_id, c.game, c.filename, c.duration,
                  c.size_bytes, c.mtime, c.has_thumb, c.favorite, c.storage,
                  snippet(segments_fts, 0, '<mark>', '</mark>', '...', 12) AS snippet,
                  bm25(segments_fts) AS score
           FROM segments_fts
           JOIN segments s ON s.id = segments_fts.rowid
           JOIN clips c ON c.id = s.clip_id
           WHERE segments_fts MATCH $q
           ORDER BY {order}
           LIMIT $limit",
        ("$q", ftsQ), ("$limit", limit ?? 100));
    return Results.Json(new Dictionary<string, object?>
    {
        ["query"] = q, ["fts"] = ftsQ, ["results"] = rows,
    });
});

// ---------- folders ----------
app.MapGet("/api/folders", () =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    var folders = con.Query(
        @"SELECT f.id, f.name, f.created_at,
                 (SELECT COUNT(*) FROM folder_clips fc WHERE fc.folder_id = f.id) AS clip_count
          FROM folders f ORDER BY f.name COLLATE NOCASE");
    foreach (var f in folders)
    {
        var ids = con.Query(
            "SELECT clip_id FROM folder_clips WHERE folder_id=$id ORDER BY added_at DESC LIMIT 4",
            ("$id", f["id"]));
        f["sample_clip_ids"] = ids.Select(r => r["clip_id"]).ToList();
    }
    return Results.Json(folders);
});

app.MapPost("/api/folders", (FolderPayload body) =>
{
    Heartbeat.Touch();
    var name = body.name?.Trim() ?? "";
    if (string.IsNullOrEmpty(name)) return Detail(400, "Nazwa folderu jest wymagana.");
    if (name.Length > 100) return Detail(400, "Nazwa folderu za długa (max 100 znaków).");
    using var con = Db.Open();
    try
    {
        con.Exec("INSERT INTO folders(name) VALUES($n)", ("$n", name));
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) // CONSTRAINT
    {
        return Detail(409, $"Folder o nazwie „{name}\" już istnieje.");
    }
    var newId = con.ScalarLong("SELECT last_insert_rowid()");
    var row = con.QueryOne("SELECT id, name, created_at FROM folders WHERE id=$id", ("$id", newId));
    return Results.Json(row);
});

app.MapPatch("/api/folders/{folderId:long}", (long folderId, FolderPayload body) =>
{
    Heartbeat.Touch();
    var name = body.name?.Trim() ?? "";
    if (string.IsNullOrEmpty(name)) return Detail(400, "Nazwa folderu jest wymagana.");
    using var con = Db.Open();
    if (con.QueryOne("SELECT id FROM folders WHERE id=$id", ("$id", folderId)) is null)
        return Detail(404, "Folder nie istnieje.");
    try
    {
        con.Exec("UPDATE folders SET name=$n WHERE id=$id", ("$n", name), ("$id", folderId));
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
    {
        return Detail(409, $"Folder o nazwie „{name}\" już istnieje.");
    }
    return Results.Json(new { ok = true, id = folderId, name });
});

app.MapDelete("/api/folders/{folderId:long}", (long folderId) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    var row = con.QueryOne("SELECT name FROM folders WHERE id=$id", ("$id", folderId));
    if (row is null) return Detail(404, "Folder nie istnieje.");
    con.Exec("DELETE FROM folders WHERE id=$id", ("$id", folderId));
    return Results.Json(new Dictionary<string, object?> { ["ok"] = true, ["deleted_name"] = row["name"] });
});

var folderClipSorts = new Dictionary<string, string>
{
    ["newest"] = "c.mtime DESC",
    ["oldest"] = "c.mtime ASC",
    ["largest"] = "c.size_bytes DESC",
    ["smallest"] = "c.size_bytes ASC",
    ["added"] = "fc.added_at DESC",
};

app.MapGet("/api/folders/{folderId:long}/clips", (long folderId, string? sort, int? limit) =>
{
    Heartbeat.Touch();
    var order = folderClipSorts.GetValueOrDefault(sort ?? "newest", folderClipSorts["added"]);
    using var con = Db.Open();
    var folder = con.QueryOne("SELECT id, name, created_at FROM folders WHERE id=$id", ("$id", folderId));
    if (folder is null) return Detail(404, "Folder nie istnieje.");
    var rows = con.Query(
        $@"SELECT c.id, c.game, c.filename, c.duration, c.size_bytes, c.mtime,
                  c.has_thumb, c.transcribed_at, c.storage
           FROM folder_clips fc JOIN clips c ON c.id = fc.clip_id
           WHERE fc.folder_id = $id ORDER BY {order} LIMIT $limit",
        ("$id", folderId), ("$limit", limit ?? 500));
    return Results.Json(new Dictionary<string, object?> { ["folder"] = folder, ["clips"] = rows });
});

app.MapPost("/api/folders/{folderId:long}/clips/{clipId:long}", (long folderId, long clipId) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    if (con.QueryOne("SELECT 1 FROM folders WHERE id=$id", ("$id", folderId)) is null)
        return Detail(404, "Folder nie istnieje.");
    if (con.QueryOne("SELECT 1 FROM clips WHERE id=$id", ("$id", clipId)) is null)
        return Detail(404, "Klip nie istnieje.");
    con.Exec("INSERT OR IGNORE INTO folder_clips(folder_id, clip_id) VALUES($f,$c)",
        ("$f", folderId), ("$c", clipId));
    return Results.Json(new { ok = true });
});

app.MapDelete("/api/folders/{folderId:long}/clips/{clipId:long}", (long folderId, long clipId) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    con.Exec("DELETE FROM folder_clips WHERE folder_id=$f AND clip_id=$c",
        ("$f", folderId), ("$c", clipId));
    return Results.Json(new { ok = true });
});

app.MapGet("/api/clips/{clipId:long}/folders", (long clipId) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    var rows = con.Query("SELECT folder_id FROM folder_clips WHERE clip_id=$id", ("$id", clipId));
    return Results.Json(new Dictionary<string, object?>
    {
        ["folder_ids"] = rows.Select(r => r["folder_id"]).ToList(),
    });
});

// ---------- segments ----------
app.MapGet("/api/segments/{clipId:long}", (long clipId) =>
{
    using var con = Db.Open();
    var clip = con.QueryOne("SELECT * FROM clips WHERE id=$id", ("$id", clipId));
    if (clip is null) return Detail(404, "clip not found");
    var segs = con.Query(
        "SELECT id, start_s, end_s, text FROM segments WHERE clip_id=$id ORDER BY start_s",
        ("$id", clipId));
    return Results.Json(new Dictionary<string, object?> { ["clip"] = clip, ["segments"] = segs });
});

app.MapPatch("/api/segments/{segmentId:long}", (long segmentId, SegmentPayload body) =>
{
    Heartbeat.Touch();
    var text = body.text?.Trim() ?? "";
    if (string.IsNullOrEmpty(text)) return Detail(422, "Tekst nie może być pusty.");
    using var con = Db.Open();
    if (con.QueryOne("SELECT id FROM segments WHERE id=$id", ("$id", segmentId)) is null)
        return Detail(404, "Segment nie istnieje.");
    con.Exec("UPDATE segments SET text=$t WHERE id=$id", ("$t", text), ("$id", segmentId));
    return Results.Json(new { ok = true, id = segmentId, text });
});

// Re-run transcription for a single clip on demand (port of /api/clips/{id}/retranscribe).
// Refused while a batch run is active so the engine isn't used concurrently.
app.MapPost("/api/clips/{clipId:long}/retranscribe", (long clipId) =>
{
    Heartbeat.Touch();
    if (TranscribeState.Snapshot().running)
        return Detail(409, "Trwa transkrypcja, spróbuj później.");

    using var con = Db.Open();
    var row = con.QueryOne("SELECT filepath FROM clips WHERE id=$id", ("$id", clipId));
    if (row is null) return Detail(404, "clip not found");
    var fp = row["filepath"] as string ?? "";
    if (!File.Exists(fp)) return Detail(404, "Plik nie istnieje na dysku.");

    var sw = Stopwatch.StartNew();
    List<Transcriber.Segment> segs;
    try { segs = Transcriber.Transcribe(fp); }
    catch (Exception ex) { return Detail(500, $"Transkrypcja nie powiodła się: {ex.Message}"); }
    sw.Stop();

    con.Exec("DELETE FROM segments WHERE clip_id=$id", ("$id", clipId));
    foreach (var s in segs)
        con.Exec("INSERT INTO segments (clip_id, start_s, end_s, text) VALUES ($c,$s,$e,$t)",
            ("$c", clipId), ("$s", s.Start), ("$e", s.End), ("$t", s.Text));
    con.Exec("UPDATE clips SET transcribed_at=$ts, language=$lang WHERE id=$id",
        ("$ts", TranscribeWorker.NowIso()), ("$lang", Config.WhisperLang), ("$id", clipId));

    return Results.Json(new Dictionary<string, object?>
    {
        ["ok"] = true,
        ["segments"] = segs.Count,
        ["seconds"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
    });
});

// ---------- media operations (fix / cut / explorer / delete) ----------
app.MapPost("/api/clips/{clipId:long}/fix", (long clipId) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    var row = con.QueryOne("SELECT filepath, filename FROM clips WHERE id=$id", ("$id", clipId));
    if (row is null) return Detail(404, "clip not found");
    var src = row["filepath"] as string ?? "";
    if (!File.Exists(src)) return Detail(404, "file missing on disk");

    // Capture original mtime so the recording date stays correct after we replace
    // the file with the ffmpeg-produced (newly written) one.
    var originalMtime = ((DateTimeOffset)File.GetLastWriteTimeUtc(src)).ToUnixTimeMilliseconds() / 1000.0;

    var (ok, fixedPath, message, trimmed) = Media.FixBrokenClip(src);
    if (!ok || fixedPath is null) return Detail(422, message);

    // Move original to Recycle Bin, then slot the fixed file into its place.
    try { Trash.Send(src); }
    catch (Exception ex)
    {
        try { if (File.Exists(fixedPath)) File.Delete(fixedPath); } catch { /* don't strand tmp */ }
        return Detail(500, $"Nie udało się przenieść oryginału do Kosza: {ex.Message}");
    }
    try { File.Move(fixedPath, src, overwrite: true); }
    catch (IOException ex) { return Detail(500, $"Nie udało się przenieść naprawionego pliku: {ex.Message}"); }

    // Restore the original mtime so the UI keeps showing the real recording date.
    try { File.SetLastWriteTimeUtc(src, DateTimeOffset.FromUnixTimeMilliseconds((long)(originalMtime * 1000)).UtcDateTime); }
    catch (IOException) { }

    // Refresh metadata + thumbnail.
    var newDuration = Media.ProbeDuration(src);
    var tp = Config.ThumbPath(clipId);
    if (File.Exists(tp)) { try { File.Delete(tp); } catch (IOException) { } }
    var hasThumb = Media.MakeThumbnail(clipId, src);

    // Shift existing transcript segments by -trimmed so timestamps line up with the
    // trimmed file. Drop anything that landed entirely in the cut-off intro.
    int shifted = 0, dropped = 0;
    if (trimmed > 0)
    {
        foreach (var s in con.Query("SELECT id, start_s, end_s FROM segments WHERE clip_id=$id", ("$id", clipId)))
        {
            var sid = Convert.ToInt64(s["id"]);
            var newStart = Convert.ToDouble(s["start_s"]) - trimmed;
            var newEnd = Convert.ToDouble(s["end_s"]) - trimmed;
            if (newEnd <= 0)
            {
                con.Exec("DELETE FROM segments WHERE id=$id", ("$id", sid));
                dropped++;
            }
            else
            {
                con.Exec("UPDATE segments SET start_s=$st, end_s=$en WHERE id=$id",
                    ("$st", Math.Max(0.0, newStart)), ("$en", newEnd), ("$id", sid));
                shifted++;
            }
        }
    }

    var stat = new FileInfo(src);
    con.Exec("UPDATE clips SET size_bytes=$sz, mtime=$mt, duration=$du, has_thumb=$ht WHERE id=$id",
        ("$sz", stat.Length), ("$mt", originalMtime), ("$du", newDuration),
        ("$ht", hasThumb ? 1 : 0), ("$id", clipId));

    return Results.Json(new Dictionary<string, object?>
    {
        ["ok"] = true,
        ["message"] = message,
        ["trimmed_seconds"] = trimmed,
        ["new_duration"] = newDuration,
        ["new_size_bytes"] = stat.Length,
        ["segments_shifted"] = shifted,
        ["segments_dropped"] = dropped,
    });
});

app.MapPost("/api/clips/{clipId:long}/cut", (long clipId, CutPayload body) =>
{
    Heartbeat.Touch();
    using var con = Db.Open();
    var row = con.QueryOne("SELECT filepath, filename, game FROM clips WHERE id=$id", ("$id", clipId));
    if (row is null) return Detail(404, "Klip nie istnieje.");
    var src = row["filepath"] as string ?? "";
    if (!File.Exists(src)) return Detail(404, "Plik źródłowy nie istnieje na dysku.");

    var cutsRoot = Settings.GetCutsRoot();
    Directory.CreateDirectory(cutsRoot);

    // Build output filename: <stem>_cut_<start>-<end>[_<sizeMB>MB].mp4
    var stem = Path.GetFileNameWithoutExtension(src);
    var sLbl = ((int)body.start).ToString(CultureInfo.InvariantCulture);
    var eLbl = ((int)body.end).ToString(CultureInfo.InvariantCulture);
    var sizeLbl = body.target_size_mb is > 0 ? $"_{(int)body.target_size_mb.Value}MB" : "";
    var outName = $"{stem}_cut_{sLbl}-{eLbl}{sizeLbl}.mp4";
    // Sanitize — Windows doesn't like certain chars.
    outName = Regex.Replace(outName, "[<>:\"/\\\\|?*]", "_");
    var output = Path.Combine(cutsRoot, outName);

    // Avoid overwriting an existing cut.
    var baseName = Path.GetFileNameWithoutExtension(output);
    for (int i = 2; File.Exists(output); i++)
        output = Path.Combine(cutsRoot, $"{baseName}_{i}.mp4");

    var (ok, msg, stats) = Media.CutClip(src, output, body.start, body.end, body.target_size_mb);
    if (!ok) return Detail(422, msg);

    // If the cut landed inside the clips root, register it right away so it's
    // accessible in-app without a manual rescan.
    long? newClipId = null;
    var clipsRoot = Settings.GetClipsRoot();
    bool insideLibrary;
    try
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(clipsRoot));
        insideLibrary = Path.GetFullPath(output)
            .StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    catch { insideLibrary = false; }

    if (insideLibrary)
    {
        var outFi = new FileInfo(output);
        var parent = Path.GetDirectoryName(output)!;
        var clipsRootNorm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(clipsRoot));
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) == clipsRootNorm
            ? "_root" : new DirectoryInfo(parent).Name;
        var mtime = ((DateTimeOffset)outFi.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
        con.Exec("INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES($g,$f,$p,$s,$m)",
            ("$g", game), ("$f", outFi.Name), ("$p", output), ("$s", outFi.Length), ("$m", mtime));
        newClipId = con.ScalarLong("SELECT last_insert_rowid()");
    }

    var resp = new Dictionary<string, object?>
    {
        ["ok"] = true,
        ["output_path"] = output,
        ["output_name"] = Path.GetFileName(output),
        ["cuts_root"] = cutsRoot,
        ["clip_id"] = newClipId,
        ["in_library"] = newClipId is not null,
    };
    foreach (var kv in stats) resp[kv.Key] = kv.Value; // **stats
    return Results.Json(resp);
});

app.MapPost("/api/show-in-explorer", (ShowPathPayload body) =>
{
    Heartbeat.Touch();
    var p = body.path ?? "";
    if (!Path.Exists(p)) return Detail(404, "Ścieżka nie istnieje.");
    try
    {
        var psi = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = false };
        // /select highlights the file inside its folder; a bare path opens the folder.
        psi.ArgumentList.Add(File.Exists(p) ? $"/select,{p}" : p);
        Process.Start(psi);
    }
    catch (Exception ex) { return Detail(500, $"Nie udało się otworzyć Eksploratora: {ex.Message}"); }
    return Results.Json(new { ok = true });
});

app.MapDelete("/api/clips/{clipId:long}", async (long clipId, bool? delete_file) =>
{
    var deleteFile = delete_file ?? true;
    using var con = Db.Open();
    var row = con.QueryOne(
        "SELECT filepath, filename, storage, remote_id FROM clips WHERE id=$id", ("$id", clipId));
    if (row is null) return Detail(404, "clip not found");
    var filepath = row["filepath"] as string ?? "";
    var filename = row["filename"];

    string? trashError = null;
    if (deleteFile && File.Exists(filepath))
        trashError = Trash.SendWithRetry(filepath);

    var tp = Config.ThumbPath(clipId);
    if (File.Exists(tp)) { try { File.Delete(tp); } catch (IOException) { } }

    con.Exec("DELETE FROM clips WHERE id=$id", ("$id", clipId)); // CASCADE -> segments

    // When the clip lived in Drive, move its Drive copy to trash too (recoverable).
    if ((row["storage"] as string) == "cloud" && row["remote_id"] is string rid && rid.Length > 0)
        await CloudService.TryTrashRemoteAsync(rid);

    return Results.Json(new Dictionary<string, object?>
    {
        ["deleted_clip_id"] = clipId,
        ["filename"] = filename,
        ["file_sent_to_trash"] = deleteFile && trashError is null,
        ["trash_error"] = trashError,
    });
});

// ---------- thumbnails ----------
app.MapGet("/thumb/{clipId:long}", (long clipId) =>
{
    var p = Config.ThumbPath(clipId);
    if (!File.Exists(p))
    {
        // Lazy generation: look up the source path + has_thumb.
        using var con = Db.Open();
        var row = con.QueryOne("SELECT filepath, has_thumb FROM clips WHERE id=$id", ("$id", clipId));
        // Clip not found, or we already tried and failed (has_thumb == 2) → 404 straight away.
        if (row is null || Convert.ToInt64(row["has_thumb"] ?? 0L) == 2)
            return Detail(404, "no thumbnail");
        if (!Media.MakeThumbnail(clipId, row["filepath"] as string ?? ""))
        {
            // Remember the failure so we don't retry on every scroll.
            con.Exec("UPDATE clips SET has_thumb=2 WHERE id=$id", ("$id", clipId));
            return Detail(404, "no thumbnail");
        }
        con.Exec("UPDATE clips SET has_thumb=1 WHERE id=$id", ("$id", clipId));
    }
    return Results.File(p, "image/jpeg");
});

// ---------- cloud (Google Drive offload) ----------
// Aternos-style single embedded OAuth client: app creds live in gitignored
// data/google_client.json; the per-user refresh token lives in Windows Credential
// Manager. CloudService owns the OAuth + Drive orchestration.
app.MapGet("/api/cloud/status", async () =>
{
    Heartbeat.Touch();
    return Results.Json(await CloudService.StatusAsync());
});

app.MapPost("/api/cloud/connect", () =>
{
    Heartbeat.Touch();
    if (!CloudService.IsConfigured())
        return Detail(400, "Brak pliku google_client.json w folderze data.");
    try { return Results.Json(new { auth_url = CloudService.BeginConnect() }); }
    catch (Exception ex) { return Detail(500, ex.Message); }
});

app.MapPost("/api/cloud/disconnect", async () =>
{
    Heartbeat.Touch();
    await CloudService.DisconnectAsync();
    return Results.Json(new { ok = true });
});

app.MapPost("/api/clips/{clipId:long}/upload", async (long clipId) =>
{
    Heartbeat.Touch();
    if (!CloudService.IsConnected()) return Detail(400, "Nie połączono z Google Drive.");
    try
    {
        bool already = await CloudService.UploadClipAsync(clipId);
        return Results.Json(new { ok = true, already });
    }
    catch (FileNotFoundException ex) { return Detail(404, ex.Message); }
    catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
    {
        return Detail(401, "Wygasło połączenie z Google Drive — połącz ponownie.");
    }
    catch (Exception ex) { return Detail(500, ex.Message); }
});

app.MapPost("/api/clips/{clipId:long}/download", async (long clipId) =>
{
    Heartbeat.Touch();
    if (!CloudService.IsConnected()) return Detail(400, "Nie połączono z Google Drive.");
    try
    {
        bool already = await CloudService.DownloadClipAsync(clipId);
        return Results.Json(new { ok = true, already });
    }
    catch (FileNotFoundException ex) { return Detail(404, ex.Message); }
    catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
    {
        return Detail(401, "Wygasło połączenie z Google Drive — połącz ponownie.");
    }
    catch (Exception ex) { return Detail(500, ex.Message); }
});

app.MapPost("/api/folders/{folderId:long}/upload", async (long folderId) =>
{
    Heartbeat.Touch();
    if (!CloudService.IsConnected()) return Detail(400, "Nie połączono z Google Drive.");
    try
    {
        var (uploaded, total, skipped, failed) = await CloudService.UploadFolderAsync(folderId);
        return Results.Json(new { uploaded, total, skipped, failed });
    }
    catch (Exception ex) { return Detail(500, ex.Message); }
});

// ---------- video (local range streaming; cloud clips proxied from Drive) ----------
app.MapGet("/video/{clipId:long}", async (long clipId, HttpContext http) =>
{
    using var con = Db.Open();
    var row = con.QueryOne("SELECT filepath, storage, remote_id FROM clips WHERE id=$id", ("$id", clipId));
    if (row is null) return Detail(404, "clip not found");
    if ((row["storage"] as string) == "cloud")
    {
        var remoteId = row["remote_id"] as string;
        if (string.IsNullOrEmpty(remoteId)) return Detail(404, "cloud clip has no remote id");
        await CloudService.ProxyVideoAsync(http, remoteId);
        return Results.Empty; // response already written by the proxy
    }
    var path = row["filepath"] as string;
    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Detail(404, "file missing on disk");
    return Results.File(path, VideoContentType(path), enableRangeProcessing: true);
});

// ---------- run ----------
// Documented contract (the frontend sends a heartbeat every 30 s): the server exits after
// 5 min with no heartbeat — but never mid-transcription. In desktop mode this is only a
// backstop; closing the window is the normal exit path.
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

// ---------- request bodies ----------
record ConfigPayload(string clips_root);
record ReplayConfigPayload(bool? enabled, int? duration_s, int? fps, string? quality, string? hotkey, bool? mic);
record FolderPayload(string name);
record SegmentPayload(string text);
record CutPayload(double start, double end, double? target_size_mb = null);
record ShowPathPayload(string path);
