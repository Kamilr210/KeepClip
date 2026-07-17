using System.Diagnostics;
using System.Runtime.InteropServices;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace KeepClip.Infrastructure;

public static class Transcriber
{
    private static readonly object ModelLock = new();
    private static WhisperFactory? _factory;

    public readonly record struct Segment(double Start, double End, string Text);

    public static List<Segment> Transcribe(
        string path,
        Action<double, string>? progress = null)
    {
        progress?.Invoke(0.02, "audio");
        var samples = LoadAudioNormalized(path)
            ?? throw new InvalidOperationException($"Nie udało się zdekodować audio: {path}");

        var factory = GetFactory();
        progress?.Invoke(0.15, "transcribing");

        // Znaczniki czasu tokenów są potrzebne do dzielenia wypowiedzi na fragmenty po ciszy.
        using var processor = factory.CreateBuilder()
            .WithLanguage(Config.WhisperLang)
            .WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5))
            .WithNoContext()
            .WithTokenTimestamps()
            .WithNoSpeechThreshold(0.85f)
            .Build();

        var outSegs = new List<Segment>();
        var audioDuration = samples.Length / 16000.0;
        foreach (var seg in ProcessAll(processor, samples, audioDuration, progress))
            SplitOnInternalGaps(seg, Config.WhisperSplitGapSeconds, outSegs);
        progress?.Invoke(0.97, "saving");
        return outSegs;
    }

    private static WhisperFactory GetFactory()
    {
        lock (ModelLock)
        {
            if (_factory is not null) return _factory;
            EnsureModelFile();
            _factory = WhisperFactory.FromPath(Config.WhisperModelPath);
            ReportLoadedRuntime();
            return _factory;
        }
    }

    // Zapisuje wybrany mechanizm obliczeniowy, aby rozpoznać użycie GPU albo CPU.
    private static void ReportLoadedRuntime()
    {
        string label = RuntimeOptions.LoadedLibrary switch
        {
            RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 => "CUDA (GPU NVIDIA)",
            RuntimeLibrary.Vulkan                        => "Vulkan (GPU)",
            RuntimeLibrary.CoreML                        => "CoreML (GPU Apple)",
            RuntimeLibrary.OpenVino                      => "OpenVINO (GPU Intel)",
            RuntimeLibrary.Cpu                           => "CPU",
            RuntimeLibrary.CpuNoAvx                      => "CPU (bez AVX)",
            _                                            => "nieznany",
        };
        string msg = $"Silnik STT: {label} [model: {Config.WhisperGgmlType}]";
        Console.Error.WriteLine(msg);
        TranscribeState.PushLog(msg);
    }

    private static void EnsureModelFile()
    {
        if (File.Exists(Config.WhisperModelPath)) return;
        Directory.CreateDirectory(Config.ModelsDir);

        // Plik .part zapobiega uznaniu przerwanego pobierania za gotowy model.
        var part = Config.WhisperModelPath + ".part";
        try
        {
            using (var modelStream = WhisperGgmlDownloader.Default
                       .GetGgmlModelAsync(ResolveGgmlType(Config.WhisperGgmlType)).GetAwaiter().GetResult())
            using (var file = File.Create(part))
                modelStream.CopyToAsync(file).GetAwaiter().GetResult();
            File.Move(part, Config.WhisperModelPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            throw;
        }
    }

    private static GgmlType ResolveGgmlType(string name) => name switch
    {
        "tiny"          => GgmlType.Tiny,
        "base"          => GgmlType.Base,
        "small"         => GgmlType.Small,
        "medium"        => GgmlType.Medium,
        "large-v1"      => GgmlType.LargeV1,
        "large-v2"      => GgmlType.LargeV2,
        "large-v3"      => GgmlType.LargeV3,
        _               => GgmlType.LargeV3Turbo,
    };

    private static List<SegmentData> ProcessAll(
        WhisperProcessor processor,
        float[] samples,
        double audioDuration,
        Action<double, string>? progress)
        => Task.Run(async () =>
        {
            var list = new List<SegmentData>();
            await foreach (var seg in processor.ProcessAsync(samples))
            {
                list.Add(seg);
                if (audioDuration > 0)
                {
                    var ratio = Math.Clamp(seg.End.TotalSeconds / audioDuration, 0, 1);
                    progress?.Invoke(0.15 + ratio * 0.8, "transcribing");
                }
            }
            return list;
        }).GetAwaiter().GetResult();

    // Dzieli wynik po dłuższej ciszy. Przy niespójnych czasach tokenów zachowuje cały segment.
    private static void SplitOnInternalGaps(SegmentData seg, double maxGap, List<Segment> outSegs)
    {
        double segStart = seg.Start.TotalSeconds;
        double segEnd = seg.End.TotalSeconds;

        // Pomija tokeny specjalne; czasy Whispera są zapisane w setnych częściach sekundy.
        var toks = new List<(double Start, double End, string Text)>();
        if (seg.Tokens is not null)
        {
            foreach (var t in seg.Tokens)
            {
                var txt = t.Text;
                if (string.IsNullOrEmpty(txt) || txt.StartsWith("[_", StringComparison.Ordinal))
                    continue;
                toks.Add((t.Start / 100.0, t.End / 100.0, txt));
            }
        }

        // Dzielenie jest bezpieczne tylko wtedy, gdy zegar tokenów zgadza się z segmentem.
        bool aligned = toks.Count > 0
            && Math.Abs(toks[0].Start - segStart) < 1.0
            && Math.Abs(toks[^1].End - segEnd) < 1.0;
        if (!aligned)
        {
            var whole = seg.Text.Trim();
            if (whole.Length > 0) outSegs.Add(new Segment(segStart, segEnd, whole));
            return;
        }

        var chunk = new List<(double Start, double End, string Text)>();
        void Flush()
        {
            if (chunk.Count == 0) return;
            var text = string.Concat(chunk.Select(c => c.Text)).Trim();
            if (text.Length > 0)
                outSegs.Add(new Segment(chunk[0].Start, chunk[^1].End, text));
            chunk.Clear();
        }

        foreach (var w in toks)
        {
            if (chunk.Count > 0 && (w.Start - chunk[^1].End) >= maxGap)
                Flush();
            chunk.Add(w);
        }
        Flush();
    }

    private static float[]? LoadAudioNormalized(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
        {
            "-v", "error",
            "-i", path,
            "-af", "dynaudnorm=f=150:g=15",
            "-vn", "-ac", "1", "-ar", "16000",
            "-f", "s16le", "-",
        }) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try { if (!proc.Start()) return null; }
        catch { return null; }

        // stderr trzeba opróżniać równolegle, aby pełny potok nie zablokował FFmpeg.
        using var pcm = new MemoryStream();
        var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(pcm);
        var errTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(300_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return null;
        }
        try { copyTask.GetAwaiter().GetResult(); _ = errTask.GetAwaiter().GetResult(); }
        catch { return null; }
        proc.WaitForExit();

        if (proc.ExitCode != 0) return null;

        int byteLen = (int)pcm.Length;
        if (byteLen < 2) return null;

        // Windows używa little-endian, więc bufor s16le można bezpośrednio odczytać jako Int16.
        int n = byteLen / 2;
        var shorts = MemoryMarshal.Cast<byte, short>(pcm.GetBuffer().AsSpan(0, n * 2));
        var samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = shorts[i] / 32768f;
        return samples;
    }
}
