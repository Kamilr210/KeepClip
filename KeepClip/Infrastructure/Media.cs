using NAudio.Wave;

namespace KeepClip.Infrastructure;

public static class Media
{
    public readonly record struct PlaybackVideoInfo(string Codec, double Fps, int Width, int Height);
    private static readonly MediaProcessor Processor = new(new MediaProcessRunner());

    static Media()
    {
        Directory.CreateDirectory(Config.ThumbsDir);
        Directory.CreateDirectory(Config.TmpDir);
    }

    public static double? ProbeDuration(string path) => Processor.ProbeDuration(path);
    public static PlaybackVideoInfo? ProbePlaybackVideo(string path) => Processor.ProbePlaybackVideo(path);
    public static (bool Ok, string Message) CreatePlaybackProxy(string source, string output, double duration, Action<double>? progress = null) => Processor.CreatePlaybackProxy(source, output, duration, progress);
    public static int ProbeDecodeErrors(string path, double seconds = 5.0) => Processor.ProbeDecodeErrors(path, seconds);
    public static bool TryRemux(string src, string dst, double skipSeconds = 0.0) => Processor.TryRemux(src, dst, skipSeconds);
    public static (bool Ok, string? Output, string Message, double Trimmed) FixBrokenClip(string src, string? tmpOut = null) => Processor.FixBrokenClip(src, tmpOut);
    public static (bool Ok, string Message, Dictionary<string, object?> Stats) CutClip(string src, string output, double start, double end, double? targetSizeMb = null, Action<double, string>? progress = null, double audioVolume = 1.0) => Processor.CutClip(src, output, start, end, targetSizeMb, progress, audioVolume);
    public static bool MakeThumbnail(long clipId, string video, double atSeconds = 5.0) => Processor.MakeThumbnail(clipId, video, atSeconds);

    public static double[]? ExtractAudioWaveform(string path, int buckets = 180, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || buckets <= 0) return null;
        return ExtractWaveformViaFfmpeg(path, buckets, ct)
            ?? ExtractWaveformViaMediaFoundation(path, buckets, ct);
    }

    private static double[]? ExtractWaveformViaFfmpeg(string path, int buckets, CancellationToken ct)
    {
        if (!File.Exists(Config.Ffmpeg)) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Config.Ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var fullPath = Path.GetFullPath(path);
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", fullPath, "-vn", "-ac", "1", "-ar", "2000", "-f", "s16le", "-" })
                psi.ArgumentList.Add(arg);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { } });
            _ = proc.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);

            using var ms = new MemoryStream();
            var buf = new byte[16384];
            const int maxBytes = 20 * 1024 * 1024;
            int read;
            while (!ct.IsCancellationRequested && ms.Length < maxBytes && (read = proc.StandardOutput.BaseStream.Read(buf, 0, buf.Length)) > 0)
                ms.Write(buf, 0, read);

            try { if (!proc.HasExited) proc.WaitForExit(3000); } catch { }
            if (ct.IsCancellationRequested || ms.Length < 8) return null;

            return ComputeBucketsFromPcm16(ms.GetBuffer(), (int)(ms.Length / 2), buckets);
        }
        catch
        {
            return null;
        }
    }

    private static double[]? ExtractWaveformViaMediaFoundation(string path, int buckets, CancellationToken ct)
    {
        try
        {
            using var reader = new NAudio.Wave.MediaFoundationReader(path);
            var sp = reader.ToSampleProvider();
            int channels = sp.WaveFormat.Channels;
            int sr = sp.WaveFormat.SampleRate;
            long estSamples = Math.Max(1, (long)(reader.TotalTime.TotalSeconds * sr));
            int stride = Math.Max(1, (int)(estSamples / (buckets * 80L)));

            var mono = new List<float>(Math.Min(200_000, buckets * 120));
            var buf = new float[8192 * Math.Max(1, channels)];
            int frameIdx = 0;
            int read;
            while (!ct.IsCancellationRequested && (read = sp.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i + channels <= read; i += channels)
                {
                    if ((frameIdx++ % stride) != 0) continue;
                    float sum = 0;
                    for (int c = 0; c < channels; c++) sum += Math.Abs(buf[i + c]);
                    mono.Add(sum / channels);
                }
            }
            if (ct.IsCancellationRequested || mono.Count < 4) return null;

            var result = new double[buckets];
            double maxPeak = 0;
            for (int b = 0; b < buckets; b++)
            {
                int s0 = (int)((long)b * mono.Count / buckets);
                int s1 = Math.Clamp((int)((long)(b + 1) * mono.Count / buckets), s0 + 1, mono.Count);
                double sumSq = 0, peak = 0;
                int count = 0;
                for (int s = s0; s < s1; s++)
                {
                    double v = mono[s];
                    sumSq += v * v;
                    if (v > peak) peak = v;
                    count++;
                }
                double rms = count > 0 ? Math.Sqrt(sumSq / count) : 0;
                double level = rms * 0.6 + peak * 0.4;
                result[b] = level;
                if (level > maxPeak) maxPeak = level;
            }
            return NormalizeBuckets(result, maxPeak);
        }
        catch
        {
            return null;
        }
    }

    private static double[] ComputeBucketsFromPcm16(byte[] raw, int totalSamples, int buckets)
    {
        var result = new double[buckets];
        double maxPeak = 0;
        for (int b = 0; b < buckets; b++)
        {
            int s0 = (int)((long)b * totalSamples / buckets);
            int s1 = Math.Clamp((int)((long)(b + 1) * totalSamples / buckets), s0 + 1, totalSamples);
            double sumSq = 0, peak = 0;
            int count = 0;
            for (int s = s0; s < s1; s++)
            {
                short sample = (short)(raw[s * 2] | (raw[s * 2 + 1] << 8));
                double v = Math.Abs(sample) / 32768.0;
                sumSq += v * v;
                if (v > peak) peak = v;
                count++;
            }
            double rms = count > 0 ? Math.Sqrt(sumSq / count) : 0;
            double level = rms * 0.6 + peak * 0.4;
            result[b] = level;
            if (level > maxPeak) maxPeak = level;
        }
        return NormalizeBuckets(result, maxPeak);
    }

    private static double[] NormalizeBuckets(double[] result, double maxPeak)
    {
        if (maxPeak > 0.002)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] = Math.Clamp(Math.Pow(result[i] / maxPeak, 0.72), 0.0, 1.0);
        }
        return result;
    }

    internal static string? ValidateCut(double start, double end, double? targetSizeMb)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0)
            return "Początek i koniec muszą być skończonymi liczbami, a początek nie może być ujemny.";
        if (end - start <= 0.1) return "Koniec musi być co najmniej 0.1s po początku.";
        if (end - start > 60 * 30) return "Maksymalnie 30 minut.";
        if (targetSizeMb is double size && (!double.IsFinite(size) || size <= 0 || size >= long.MaxValue / 1_000_000.0))
            return "Docelowy rozmiar musi być dodatnią, skończoną liczbą MB w obsługiwanym zakresie.";
        return null;
    }

}
