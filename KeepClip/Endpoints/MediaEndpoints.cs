using System.Diagnostics;

namespace KeepClip.Endpoints;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/playback/{clipId:long}", (long clipId, bool? start, ClipRepository clips, PlaybackProxyService playback) =>
        {
            var clip = clips.GetById(clipId);
            return clip is null
                ? Api.Detail(404, "clip not found")
                : Results.Json(playback.GetOrStart(clip, start ?? true));
        });

        app.MapGet("/playback/{clipId:long}", (long clipId, ClipRepository clips, PlaybackProxyService playback) =>
        {
            var clip = clips.GetById(clipId);
            if (clip is null) return Api.Detail(404, "clip not found");
            if (!playback.TryGetReadyPath(clip, out var path))
                return Api.Detail(404, "playback preview not ready");
            return Results.File(path, "video/mp4", enableRangeProcessing: true);
        });

        app.MapGet("/video/{clipId:long}", async (long clipId, HttpContext http, ClipRepository clips) =>
        {
            var clip = clips.GetById(clipId);
            if (clip is null) return Api.Detail(404, "clip not found");
            if (clip.Storage == "cloud")
            {
                if (string.IsNullOrEmpty(clip.RemoteId)) return Api.Detail(404, "cloud clip has no remote id");
                await CloudService.ProxyVideoAsync(http, clip.RemoteId);
            return Results.Empty; // Odpowiedź została już zapisana przez pośrednika.
            }
            if (string.IsNullOrEmpty(clip.Filepath) || !File.Exists(clip.Filepath))
                return Api.Detail(404, "file missing on disk");
            return Results.File(clip.Filepath, VideoContentType(clip.Filepath), enableRangeProcessing: true);
        });

        app.MapPost("/api/show-in-explorer", (ShowPathPayload body) =>
        {
            Heartbeat.Touch();
            var p = body.path ?? "";
            if (!Path.Exists(p)) return Api.Detail(404, "Ścieżka nie istnieje.");
            try
            {
                var psi = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = false };
        // Ścieżka /select otwiera folder z zaznaczonym plikiem.
                psi.ArgumentList.Add(File.Exists(p) ? $"/select,{p}" : p);
                Process.Start(psi);
            }
            catch (Exception ex) { return Api.Detail(500, $"Nie udało się otworzyć Eksploratora: {ex.Message}"); }
            return Results.Json(new { ok = true });
        });
    }

    private static string VideoContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        _ => "video/mp4",
    };
}

record ShowPathPayload(string path);
