using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeepClip.Infrastructure;

public static class Media
{
    static Media()
    {
        Directory.CreateDirectory(Config.ThumbsDir);
        Directory.CreateDirectory(Config.TmpDir);
    }

    private readonly record struct RunResult(bool Faulted, int ExitCode, string StdOut, string StdErr);

    private static RunResult Run(string exe, IReadOnlyList<string> args, int timeoutSeconds)
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
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
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
        string src, string output, double start, double end, double? targetSizeMb = null)
    {
        var duration = end - start;
        if (duration <= 0.1) return (false, "Koniec musi być co najmniej 0.1s po początku.", new());
        if (duration > 60 * 30) return (false, "Maksymalnie 30 minut.", new());
        if (!File.Exists(src)) return (false, $"Plik źródłowy nie istnieje: {src}", new());

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        const int audioKbps = 128;
        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
    // Ustawienie pozycji przed parametrem -i jest szybkie i przy kodowaniu pozostaje dokładne.
            "-ss", start.ToString("F3", CultureInfo.InvariantCulture),
            "-to", end.ToString("F3", CultureInfo.InvariantCulture),
            "-i", src,
            "-c:v", "h264_nvenc", "-preset", "p5", "-tune", "hq",
        };

        if (targetSizeMb is > 0)
        {
            double targetBytes = targetSizeMb.Value * 1024 * 1024;
        const double overheadBytes = 50_000; // Szacowany narzut kontenera i indeksu.
            double audioBytes = (audioKbps * 1000.0 / 8) * duration;
            double videoBytesBudget = targetBytes - audioBytes - overheadBytes;
            if (videoBytesBudget <= 100_000)
                return (false,
                    $"Docelowy rozmiar {PyFloat(targetSizeMb.Value)} MB jest za mały dla " +
                    $"{duration.ToString("F1", CultureInfo.InvariantCulture)}s nagrania.",
                    new());
            int videoKbps = Math.Max(200, (int)((videoBytesBudget * 8) / duration / 1000));
            args.AddRange(new[]
            {
                "-rc", "vbr",
                "-b:v", $"{videoKbps}k",
                "-maxrate", $"{(int)(videoKbps * 1.3)}k",
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

        var r = Run(Config.Ffmpeg, args, timeoutSeconds: 600);
        if (r.Faulted)
            return (false, "ffmpeg padło: przekroczono limit czasu lub nie udało się uruchomić.", new());
        if (r.ExitCode != 0 || !File.Exists(output))
        {
            var lines = r.StdErr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var msg = lines.Length > 0 ? lines[^1] : "nieznany";
            return (false, $"ffmpeg zakończył się błędem: {msg}", new());
        }

        var fi = new FileInfo(output);
        return (true, "ok", new Dictionary<string, object?>
        {
            ["duration_s"] = duration,
            ["size_bytes"] = fi.Length,
            ["size_mb"] = Math.Round(fi.Length / 1024.0 / 1024.0, 2),
        });
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
}
