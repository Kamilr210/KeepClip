using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeepClip.Infrastructure;

public static class Media
{
    public readonly record struct PlaybackVideoInfo(string Codec, double Fps, int Width, int Height);

    static Media()
    {
        Directory.CreateDirectory(Config.ThumbsDir);
        Directory.CreateDirectory(Config.TmpDir);
    }

    private readonly record struct RunResult(bool Faulted, int ExitCode, string StdOut, string StdErr);

    private static RunResult Run(
        string exe,
        IReadOnlyList<string> args,
        int timeoutSeconds,
        Action<string>? onOutputLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var so = new StringBuilder();
        var se = new StringBuilder();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            so.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
        };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };

        try { proc.Start(); }
        catch { return new RunResult(true, -1, "", ""); }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new RunResult(true, -1, so.ToString(), se.ToString());
        }
        proc.WaitForExit(); // Pozwala zakończyć asynchroniczny odczyt obu strumieni.
        return new RunResult(false, proc.ExitCode, so.ToString(), se.ToString());
    }

    public static double? ProbeDuration(string path)
    {
        var r = Run(Config.Ffprobe, new[]
        {
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "json",
            path,
        }, timeoutSeconds: 30);
        if (r.Faulted || r.ExitCode != 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            var prop = doc.RootElement.GetProperty("format").GetProperty("duration");
            return prop.ValueKind == JsonValueKind.String
                ? double.Parse(prop.GetString()!, CultureInfo.InvariantCulture)
                : prop.GetDouble();
        }
        catch { return null; }
    }

    public static PlaybackVideoInfo? ProbePlaybackVideo(string path)
    {
        var r = Run(Config.Ffprobe, new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=codec_name,width,height,avg_frame_rate,r_frame_rate",
            "-of", "json",
            path,
        }, timeoutSeconds: 30);
        if (r.Faulted || r.ExitCode != 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            var stream = doc.RootElement.GetProperty("streams")[0];
            var codec = stream.TryGetProperty("codec_name", out var codecProp)
                ? codecProp.GetString() ?? ""
                : "";
            var width = stream.TryGetProperty("width", out var widthProp) ? widthProp.GetInt32() : 0;
            var height = stream.TryGetProperty("height", out var heightProp) ? heightProp.GetInt32() : 0;
            var fpsText = stream.TryGetProperty("avg_frame_rate", out var avgProp)
                ? avgProp.GetString()
                : null;
            var fps = ParseRate(fpsText);
            if (fps <= 0 && stream.TryGetProperty("r_frame_rate", out var rawProp))
                fps = ParseRate(rawProp.GetString());
            return new PlaybackVideoInfo(codec, fps, width, height);
        }
        catch { return null; }
    }

    public static (bool Ok, string Message) CreatePlaybackProxy(
        string source,
        string output,
        double duration,
        Action<double>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var videoInfo = ProbePlaybackVideo(source);
        var targetWidth = videoInfo is { Width: > 0 and <= 1920 } ? videoInfo.Value.Width : 1920;
        if (targetWidth % 2 != 0) targetWidth--;
        var temp = Path.Combine(
            Path.GetDirectoryName(output)!,
            $".{Path.GetFileNameWithoutExtension(output)}_{Guid.NewGuid():N}.tmp.mp4");

        List<string> BuildArgs(bool hardware)
        {
            var args = new List<string>
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-progress", "pipe:1", "-nostats",
            };

            if (hardware)
                args.AddRange(new[] { "-hwaccel", "cuda", "-hwaccel_output_format", "cuda" });

            args.AddRange(new[] { "-i", source, "-map", "0:v:0", "-map", "0:a?" });

            if (hardware)
                args.AddRange(new[] { "-vf", $"scale_cuda={targetWidth}:-2", "-r", "60", "-fps_mode", "cfr" });
            else
                args.AddRange(new[] { "-vf", "scale='min(1920,iw)':-2,fps=60" });

            args.AddRange(new[] { "-pix_fmt", "yuv420p" });

            if (hardware)
            {
                args.AddRange(new[]
                {
                    "-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr",
                    "-cq", "23", "-b:v", "8M", "-maxrate", "14M", "-bufsize", "28M",
                });
            }
            else
            {
                args.AddRange(new[]
                {
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
                    "-maxrate", "14M", "-bufsize", "28M",
                });
            }

            args.AddRange(new[]
            {
                "-c:a", "aac", "-b:a", "160k",
                "-sn", "-dn", "-movflags", "+faststart",
                temp,
            });
            return args;
        }

        string lastError = "nieznany błąd ffmpeg";
        foreach (var hardware in new[] { true, false })
        {
            TryDelete(temp);
            double lastReported = 0;
            void Report(string line)
            {
                double seconds = -1;
                const string usPrefix = "out_time_us=";
                const string timePrefix = "out_time=";
                if (line.StartsWith(usPrefix, StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(usPrefix.Length), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var microseconds))
                    seconds = microseconds / 1_000_000.0;
                else if (line.StartsWith(timePrefix, StringComparison.Ordinal) &&
                    TimeSpan.TryParse(line[timePrefix.Length..], CultureInfo.InvariantCulture, out var timestamp))
                    seconds = timestamp.TotalSeconds;

                if (seconds < 0 || duration <= 0) return;
                var current = Math.Clamp(seconds / duration, 0, 0.99);
                if (current - lastReported < 0.005 && current < 0.99) return;
                lastReported = current;
                progress?.Invoke(current);
            }

            var r = Run(Config.Ffmpeg, BuildArgs(hardware), timeoutSeconds: 3600, onOutputLine: Report);
            if (!r.Faulted && r.ExitCode == 0 && File.Exists(temp) && new FileInfo(temp).Length > 1024)
            {
                File.Move(temp, output, overwrite: true);
                progress?.Invoke(1);
                return (true, "ok");
            }

            var lines = r.StdErr.Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0) lastError = lines[^1];
        }

        TryDelete(temp);
        return (false, $"Nie udało się przygotować podglądu: {lastError}");
    }

    // Klipy DVR bywają uszkodzone na początku; przeglądarki odrzucają błędne jednostki
    // NAL, choć odtwarzacze desktopowe często je tolerują.
    public static int ProbeDecodeErrors(string path, double seconds = 5.0)
    {
        var r = Run(Config.Ffprobe, new[]
        {
            "-v", "error",
            "-read_intervals", $"%+{PyFloat(seconds)}",
            "-i", path,
        }, timeoutSeconds: 60);
        if (r.Faulted) return 999;
        return string.IsNullOrWhiteSpace(r.StdErr) ? 0 : r.StdErr.Count(c => c == '\n');
    }

    public static bool TryRemux(string src, string dst, double skipSeconds = 0.0)
    {
        try { if (File.Exists(dst)) File.Delete(dst); }
        catch { }

        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "error" };
        if (skipSeconds > 0) { args.Add("-ss"); args.Add(skipSeconds.ToString("F2", CultureInfo.InvariantCulture)); }
        args.AddRange(new[]
        {
            "-i", src,
            "-c", "copy",
            "-map", "0",
            "-ignore_unknown",
            "-movflags", "+faststart",
            "-avoid_negative_ts", "make_zero",
            dst,
        });

        var r = Run(Config.Ffmpeg, args, timeoutSeconds: 600);
        if (r.Faulted || r.ExitCode != 0) return false;
        try { return File.Exists(dst) && new FileInfo(dst).Length > 1024 * 1024; }
        catch { return false; }
    }

    // Najpierw wykonuje remuks, a potem stopniowo pomija uszkodzony początek,
    // dopóki ffprobe nie przestanie zgłaszać błędów dekodowania.
    public static (bool Ok, string? Output, string Message, double Trimmed) FixBrokenClip(
        string src, string? tmpOut = null)
    {
        tmpOut ??= Path.Combine(Config.TmpDir, $"fix_{Path.GetFileNameWithoutExtension(src)}.mp4");

        var attempts = new (string Label, double Skip)[]
        {
            ("remux", 0.0),
            ("skip 1s", 1.0),
            ("skip 3s", 3.0),
            ("skip 7s", 7.0),
            ("skip 15s", 15.0),
            ("skip 25s", 25.0),
            ("skip 35s", 35.0),
            ("skip 60s", 60.0),
        };

        foreach (var (label, skip) in attempts)
        {
            if (!TryRemux(src, tmpOut, skip)) continue;
            if (ProbeDecodeErrors(tmpOut, seconds: 3.0) == 0)
                return (true, tmpOut, $"OK ({label})", skip);
        }

        try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch { }
        return (false, null, "Nie udało się — nawet po pominięciu 60 s nadal są błędy dekodowania.", 0.0);
    }

    public static (bool Ok, string Message, Dictionary<string, object?> Stats) CutClip(
        string src,
        string output,
        double start,
        double end,
        double? targetSizeMb = null,
        Action<double, string>? progress = null)
    {
        var duration = end - start;
        if (duration <= 0.1) return (false, "Koniec musi być co najmniej 0.1s po początku.", new());
        if (duration > 60 * 30) return (false, "Maksymalnie 30 minut.", new());
        if (!File.Exists(src)) return (false, $"Plik źródłowy nie istnieje: {src}", new());

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        const int audioKbps = 128;
        const int minimumVideoKbps = 50;
        const int maximumAttempts = 3;
        const long bytesPerMb = 1_000_000;
        const long sizeMarginBytes = 100_000;
        const long containerOverheadBytes = 50_000;

        List<string> BuildArgs(int? videoKbps)
        {
            var args = new List<string>
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-progress", "pipe:1", "-nostats",
                // Ustawienie pozycji przed parametrem -i jest szybkie i przy kodowaniu pozostaje dokładne.
                "-ss", start.ToString("F3", CultureInfo.InvariantCulture),
                "-to", end.ToString("F3", CultureInfo.InvariantCulture),
                "-i", src,
                "-c:v", "h264_nvenc", "-preset", "p5", "-tune", "hq",
            };

            if (videoKbps is > 0)
            {
                args.AddRange(new[]
                {
                    "-rc", "vbr",
                    "-b:v", $"{videoKbps}k",
                    "-maxrate", $"{videoKbps}k",
                    "-bufsize", $"{videoKbps * 2}k",
                });
            }
            else
            {
                // Stała jakość zachowuje obraz, ale rozmiar zależy od ilości ruchu.
                args.AddRange(new[] { "-rc", "vbr", "-cq", "19", "-b:v", "0" });
            }

            args.AddRange(new[]
            {
                "-c:a", "aac", "-b:a", $"{audioKbps}k",
                "-movflags", "+faststart",
                output,
            });
            return args;
        }

        long? hardLimitBytes = null;
        long desiredSizeBytes = 0;
        double fixedBytes = 0;
        int? targetVideoKbps = null;

        if (targetSizeMb is > 0)
        {
            hardLimitBytes = (long)Math.Floor(targetSizeMb.Value * bytesPerMb);
            desiredSizeBytes = hardLimitBytes.Value - sizeMarginBytes;
            fixedBytes = (audioKbps * 1000.0 / 8) * duration + containerOverheadBytes;
            double videoBytesBudget = desiredSizeBytes - fixedBytes;
            int calculatedVideoKbps = (int)Math.Floor(videoBytesBudget * 8 / duration / 1000);
            if (videoBytesBudget <= 100_000 || calculatedVideoKbps < minimumVideoKbps)
                return (false,
                    $"Docelowy rozmiar {PyFloat(targetSizeMb.Value)} MB jest za mały dla " +
                    $"{duration.ToString("F1", CultureInfo.InvariantCulture)}s nagrania.",
                    new());
            targetVideoKbps = calculatedVideoKbps;
        }

        int attempts = hardLimitBytes.HasValue ? maximumAttempts : 1;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var stage = attempt == 1 ? "cutting" : "cuttingRetry";
            progress?.Invoke(0.02, stage);
            double lastReported = 0;

            void ReportFfmpegProgress(string line)
            {
                double encodedSeconds = -1;
                const string microsecondsPrefix = "out_time_us=";
                const string timestampPrefix = "out_time=";
                if (line.StartsWith(microsecondsPrefix, StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(microsecondsPrefix.Length), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var microseconds))
                {
                    encodedSeconds = microseconds / 1_000_000.0;
                }
                else if (line.StartsWith(timestampPrefix, StringComparison.Ordinal) &&
                    TimeSpan.TryParse(line[timestampPrefix.Length..], CultureInfo.InvariantCulture, out var timestamp))
                {
                    encodedSeconds = timestamp.TotalSeconds;
                }

                if (encodedSeconds < 0) return;
                var current = 0.02 + Math.Clamp(encodedSeconds / duration, 0, 1) * 0.94;
                if (current - lastReported < 0.005 && current < 0.96) return;
                lastReported = current;
                progress?.Invoke(current, stage);
            }

            var r = Run(
                Config.Ffmpeg,
                BuildArgs(targetVideoKbps),
                timeoutSeconds: 600,
                onOutputLine: ReportFfmpegProgress);
            if (r.Faulted)
            {
                TryDelete(output);
                return (false, "ffmpeg padło: przekroczono limit czasu lub nie udało się uruchomić.", new());
            }
            if (r.ExitCode != 0 || !File.Exists(output))
            {
                TryDelete(output);
                var lines = r.StdErr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var msg = lines.Length > 0 ? lines[^1] : "nieznany";
                return (false, $"ffmpeg zakończył się błędem: {msg}", new());
            }

            var fi = new FileInfo(output);
            if (!hardLimitBytes.HasValue || fi.Length <= hardLimitBytes.Value)
            {
                progress?.Invoke(0.98, "saving");
                return (true, "ok", new Dictionary<string, object?>
                {
                    ["duration_s"] = duration,
                    ["size_bytes"] = fi.Length,
                    ["size_mb"] = Math.Round(fi.Length / (double)bytesPerMb, 2),
                });
            }

            if (attempt < attempts && targetVideoKbps.HasValue)
            {
                double actualVideoBytes = Math.Max(1, fi.Length - fixedBytes);
                double desiredVideoBytes = Math.Max(1, desiredSizeBytes - fixedBytes);
                int reducedKbps = (int)Math.Floor(targetVideoKbps.Value * desiredVideoBytes / actualVideoBytes * 0.97);
                targetVideoKbps = Math.Min(targetVideoKbps.Value - 1, reducedKbps);
                if (targetVideoKbps < minimumVideoKbps) break;
            }
        }

        TryDelete(output);
        return (false,
            $"Nie udało się zmieścić wycinka w limicie {PyFloat(targetSizeMb!.Value)} MB.",
            new());
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public static bool MakeThumbnail(long clipId, string video, double atSeconds = 5.0)
    {
        var outPath = Config.ThumbPath(clipId);
        var duration = ProbeDuration(video) ?? 10.0;
        var timestamp = Math.Min(atSeconds, Math.Max(0.0, duration / 2));
        var r = Run(Config.Ffmpeg, new[]
        {
            "-y",
            "-i", video,
            "-ss", timestamp.ToString("F2", CultureInfo.InvariantCulture),
            "-frames:v", "1",
            "-vf", "scale=1280:-2",
            "-q:v", "2",
            outPath,
        }, timeoutSeconds: 30);
        if (r.Faulted || r.ExitCode != 0) return false;
        return File.Exists(outPath);
    }

    private static string PyFloat(double v) =>
        v == Math.Floor(v) && !double.IsInfinity(v)
            ? v.ToString("0.0", CultureInfo.InvariantCulture)
            : v.ToString("R", CultureInfo.InvariantCulture);

    private static double ParseRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split('/', 2);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator))
            return 0;
        if (parts.Length == 1) return numerator;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
            return 0;
        return numerator / denominator;
    }
}
