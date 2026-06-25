using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KeepClip.Services;

/// <summary>
/// Business logic for clip media operations (fix / cut / delete / retranscribe). Uses the
/// repositories for persistence and the static infrastructure wrappers (Media, Trash,
/// Transcriber, CloudService) for the heavy lifting. Returns <see cref="Result{T}"/> so the
/// endpoints stay thin — they only translate it into an HTTP response.
/// </summary>
public class ClipService
{
    private readonly ClipRepository _clips;
    private readonly SegmentRepository _segments;

    public ClipService(ClipRepository clips, SegmentRepository segments)
    {
        _clips = clips;
        _segments = segments;
    }

    /// <summary>Remux/repair a broken file in place, restoring its mtime, regenerating the
    /// thumbnail, and shifting transcript segments by any trimmed-off intro.</summary>
    public Result<Dictionary<string, object?>> Fix(long clipId)
    {
        var clip = _clips.GetById(clipId);
        if (clip is null) return Fail(404, "clip not found");
        var src = clip.Filepath ?? "";
        if (!File.Exists(src)) return Fail(404, "file missing on disk");

        // Preserve the recording date across the ffmpeg-produced replacement.
        var originalMtime = ((DateTimeOffset)File.GetLastWriteTimeUtc(src)).ToUnixTimeMilliseconds() / 1000.0;

        var (ok, fixedPath, message, trimmed) = Media.FixBrokenClip(src);
        if (!ok || fixedPath is null) return Fail(422, message);

        // Move original to Recycle Bin, then slot the fixed file into its place.
        try { Trash.Send(src); }
        catch (Exception ex)
        {
            try { if (File.Exists(fixedPath)) File.Delete(fixedPath); } catch { /* don't strand tmp */ }
            return Fail(500, $"Nie udało się przenieść oryginału do Kosza: {ex.Message}");
        }
        try { File.Move(fixedPath, src, overwrite: true); }
        catch (IOException ex) { return Fail(500, $"Nie udało się przenieść naprawionego pliku: {ex.Message}"); }

        try { File.SetLastWriteTimeUtc(src, DateTimeOffset.FromUnixTimeMilliseconds((long)(originalMtime * 1000)).UtcDateTime); }
        catch (IOException) { }

        var newDuration = Media.ProbeDuration(src);
        var tp = Config.ThumbPath(clipId);
        if (File.Exists(tp)) { try { File.Delete(tp); } catch (IOException) { } }
        var hasThumb = Media.MakeThumbnail(clipId, src);

        int shifted = 0, dropped = 0;
        if (trimmed > 0) (shifted, dropped) = _segments.ShiftTimestamps(clipId, trimmed);

        var stat = new FileInfo(src);
        _clips.UpdateAfterFix(clipId, stat.Length, originalMtime, newDuration, hasThumb ? 1 : 0);

        DevLog.Add($"Naprawa klipu #{clipId}: {message} (przycięto {trimmed:0.#}s; segmenty: przesunięto {shifted}, usunięto {dropped})");
        return Ok(new()
        {
            ["ok"] = true,
            ["message"] = message,
            ["trimmed_seconds"] = trimmed,
            ["new_duration"] = newDuration,
            ["new_size_bytes"] = stat.Length,
            ["segments_shifted"] = shifted,
            ["segments_dropped"] = dropped,
        });
    }

    /// <summary>Cut/compress a fragment to clipsRoot\Wycinki, registering it in the library
    /// when it lands inside the clips root.</summary>
    public Result<Dictionary<string, object?>> Cut(long clipId, double start, double end, double? targetSizeMb)
    {
        var clip = _clips.GetById(clipId);
        if (clip is null) return Fail(404, "Klip nie istnieje.");
        var src = clip.Filepath ?? "";
        if (!File.Exists(src)) return Fail(404, "Plik źródłowy nie istnieje na dysku.");

        var cutsRoot = Settings.GetCutsRoot();
        Directory.CreateDirectory(cutsRoot);

        // Build output filename: <stem>_cut_<start>-<end>[_<sizeMB>MB].mp4 (sanitized).
        var stem = Path.GetFileNameWithoutExtension(src);
        var sLbl = ((int)start).ToString(CultureInfo.InvariantCulture);
        var eLbl = ((int)end).ToString(CultureInfo.InvariantCulture);
        var sizeLbl = targetSizeMb is > 0 ? $"_{(int)targetSizeMb.Value}MB" : "";
        var outName = Regex.Replace($"{stem}_cut_{sLbl}-{eLbl}{sizeLbl}.mp4", "[<>:\"/\\\\|?*]", "_");
        var output = Path.Combine(cutsRoot, outName);

        var baseName = Path.GetFileNameWithoutExtension(output);
        for (int i = 2; File.Exists(output); i++)
            output = Path.Combine(cutsRoot, $"{baseName}_{i}.mp4");

        var (ok, msg, stats) = Media.CutClip(src, output, start, end, targetSizeMb);
        if (!ok) return Fail(422, msg);

        // Register right away if the cut landed inside the clips root.
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
            newClipId = _clips.Insert(game, outFi.Name, output, outFi.Length, mtime);
        }

        DevLog.Add($"Wycinek: utworzono {Path.GetFileName(output)}{(newClipId is not null ? " (dodano do biblioteki)" : "")}");
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
        return Ok(resp);
    }

    /// <summary>Delete a clip: file to Recycle Bin (optional), thumbnail, DB rows, and the
    /// Drive copy when it was offloaded.</summary>
    public async Task<Result<Dictionary<string, object?>>> DeleteAsync(long clipId, bool deleteFile)
    {
        var clip = _clips.GetById(clipId);
        if (clip is null) return Fail(404, "clip not found");

        string? trashError = null;
        if (deleteFile && !string.IsNullOrEmpty(clip.Filepath) && File.Exists(clip.Filepath))
            trashError = Trash.SendWithRetry(clip.Filepath);

        var tp = Config.ThumbPath(clipId);
        if (File.Exists(tp)) { try { File.Delete(tp); } catch (IOException) { } }

        _clips.Delete(clipId); // CASCADE -> segments

        if (clip.Storage == "cloud" && clip.RemoteId is { Length: > 0 } rid)
            await CloudService.TryTrashRemoteAsync(rid);

        DevLog.Add($"Usunięto klip #{clipId} ({clip.Filename}){(deleteFile && trashError is null ? " — plik do Kosza" : "")}");
        return Ok(new()
        {
            ["deleted_clip_id"] = clipId,
            ["filename"] = clip.Filename,
            ["file_sent_to_trash"] = deleteFile && trashError is null,
            ["trash_error"] = trashError,
        });
    }

    /// <summary>Re-run Whisper for a single clip. Refused while a batch run is active.</summary>
    public Result<Dictionary<string, object?>> Retranscribe(long clipId)
    {
        if (TranscribeState.Snapshot().running)
            return Fail(409, "Trwa transkrypcja, spróbuj później.");

        var clip = _clips.GetById(clipId);
        if (clip is null) return Fail(404, "clip not found");
        var fp = clip.Filepath ?? "";
        if (!File.Exists(fp)) return Fail(404, "Plik nie istnieje na dysku.");

        var sw = Stopwatch.StartNew();
        List<Transcriber.Segment> segs;
        try { segs = Transcriber.Transcribe(fp); }
        catch (Exception ex) { return Fail(500, $"Transkrypcja nie powiodła się: {ex.Message}"); }
        sw.Stop();

        _segments.ReplaceAll(clipId, segs.Select(s => (s.Start, s.End, s.Text)));
        _clips.SetTranscribed(clipId, TranscribeWorker.NowIso(), Config.WhisperLang);

        DevLog.Add($"Transkrypcja klipu #{clipId}: gotowe — {segs.Count} segmentów w {Math.Round(sw.Elapsed.TotalSeconds, 1)} s");
        return Ok(new()
        {
            ["ok"] = true,
            ["segments"] = segs.Count,
            ["seconds"] = Math.Round(sw.Elapsed.TotalSeconds, 1),
        });
    }

    private static Result<Dictionary<string, object?>> Ok(Dictionary<string, object?> v) => Result<Dictionary<string, object?>>.Ok(v);
    private static Result<Dictionary<string, object?>> Fail(int code, string msg) => Result<Dictionary<string, object?>>.Fail(code, msg);
}
