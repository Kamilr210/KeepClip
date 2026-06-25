using System.Diagnostics;
using System.Runtime.InteropServices;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace KeepClip.Infrastructure;

/// <summary>
/// Speech-to-text engine — a port of <c>transcriber.py</c>. The Python build used
/// faster-whisper (CTranslate2); this uses Whisper.net (whisper.cpp / GGML), so the
/// transcripts are <i>functionally equivalent</i> (same model family, language and
/// tuning intent) but not bit-identical. The factory (loaded model) is a singleton;
/// a lightweight processor is built per file, exactly like the Python single-model /
/// per-call pattern.
/// </summary>
public static class Transcriber
{
    private static readonly object ModelLock = new();
    private static WhisperFactory? _factory;

    /// <summary>One searchable transcript line: [Start, End] seconds + text.</summary>
    public readonly record struct Segment(double Start, double End, string Text);

    /// <summary>
    /// Transcribe one media file to gap-split Polish segments. Throws when the audio
    /// can't be decoded (mirrors the Python worker's per-file try/except → "BŁĄD").
    /// </summary>
    public static List<Segment> Transcribe(string path)
    {
        var samples = LoadAudioNormalized(path)
            ?? throw new InvalidOperationException($"Nie udało się zdekodować audio: {path}");

        var factory = GetFactory();

        // Map the faster-whisper transcribe() parameters onto Whisper.net's builder:
        //   language="pl"                       -> WithLanguage("pl")
        //   beam_size=5                          -> WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5))
        //   condition_on_previous_text=False     -> WithNoContext()
        //   word_timestamps=True                 -> WithTokenTimestamps()  (needed for gap-splitting)
        //   no_speech_threshold=0.85             -> WithNoSpeechThreshold(0.85)
        // Not ported (no whisper.cpp equivalent / deliberate v1 simplification):
        //   no_repeat_ngram_size=3  — no builder knob in whisper.cpp.
        //   vad_filter=True (min_silence 500ms) — Whisper.net's VAD is a separate Silero
        //     model + pipeline; skipping it keeps the install small (easy-install priority).
        //     no_speech_threshold + no-context already suppress most non-speech output.
        using var processor = factory.CreateBuilder()
            .WithLanguage(Config.WhisperLang)
            .WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5))
            .WithNoContext()
            .WithTokenTimestamps()
            .WithNoSpeechThreshold(0.85f)
            .Build();

        var outSegs = new List<Segment>();
        foreach (var seg in ProcessAll(processor, samples))
            SplitOnInternalGaps(seg, Config.WhisperSplitGapSeconds, outSegs);
        return outSegs;
    }

    /// <summary>
    /// Lazily download the GGML weights (once, ~1.6 GB) and load the factory. Guarded
    /// by a lock; only the first transcription pays the download/load cost. Whisper.net
    /// auto-selects the best referenced runtime at load time — probe order
    /// Cuda→Cuda12→Vulkan→…→Cpu — so on a machine with a usable GPU this loads the
    /// Vulkan backend (we ship Vulkan + CPU runtimes), falling back to CPU otherwise.
    /// </summary>
    private static WhisperFactory GetFactory()
    {
        lock (ModelLock)
        {
            if (_factory is not null) return _factory;
            EnsureModelFile();
            _factory = WhisperFactory.FromPath(Config.WhisperModelPath);
            ReportLoadedRuntime();   // record GPU-vs-CPU once, right after the native lib binds
            return _factory;
        }
    }

    /// <summary>
    /// Whisper.net records which native runtime it actually bound in
    /// <see cref="RuntimeOptions.LoadedLibrary"/> once the factory loads. We surface it
    /// once — to the server console and the transcription log panel — so a "transcription
    /// is slow" report can be triaged at a glance: a <c>CPU</c> value means the machine
    /// had no usable GPU and fell back (the expected several-GB-RAM / high-CPU path the
    /// user hit before), whereas <c>Vulkan</c>/<c>CUDA</c> confirms GPU acceleration is live.
    /// </summary>
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

        // Download to a .part file and rename on success, so an interrupted download
        // never leaves a truncated model that would fail to load on the next run.
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
            try { if (File.Exists(part)) File.Delete(part); } catch { /* best effort */ }
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
        _               => GgmlType.LargeV3Turbo,   // default / "large-v3-turbo"
    };

    /// <summary>Drain the async segment stream into a list. Callers (the worker thread
    /// and the synchronous /retranscribe request) intentionally block, matching the
    /// Python model.transcribe() which ran a whole file to completion before returning.</summary>
    private static List<SegmentData> ProcessAll(WhisperProcessor processor, float[] samples)
        => Task.Run(async () =>
        {
            var list = new List<SegmentData>();
            await foreach (var seg in processor.ProcessAsync(samples))
                list.Add(seg);
            return list;
        }).GetAwaiter().GetResult();

    /// <summary>
    /// Port of <c>_split_on_internal_gaps</c>: break one model segment into sub-segments
    /// wherever an internal pause is at least <paramref name="maxGap"/> seconds, so each
    /// stored line is a contiguous speech run (better search granularity + jump-to UX).
    /// faster-whisper exposed clean per-word timings; whisper.cpp gives per-token timings
    /// (centiseconds) via WithTokenTimestamps(). If those aren't usable or don't line up
    /// with the segment clock, we emit the whole segment unchanged — never worse than the
    /// raw model output.
    /// </summary>
    private static void SplitOnInternalGaps(SegmentData seg, double maxGap, List<Segment> outSegs)
    {
        double segStart = seg.Start.TotalSeconds;
        double segEnd = seg.End.TotalSeconds;

        // Real word/sub-word tokens only: drop whisper.cpp special tokens ([_BEG_],
        // [_TT_123], ...). Token Start/End are int64 centiseconds → seconds = /100.
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

        // Trust token-level splitting only when the token clock matches the segment
        // clock (guards against a surprise unit/empty timings) — else fall back.
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

    /// <summary>
    /// Port of <c>_load_audio_normalized</c>: decode any container to 16 kHz mono PCM
    /// with light loudness normalization, returning float samples in [-1, 1]. Returns
    /// null on any ffmpeg failure (→ caller raises). Unlike <see cref="Media"/>'s text
    /// runner, stdout is read as raw bytes so the binary PCM stream isn't corrupted.
    /// </summary>
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

        // Read raw PCM from stdout and drain stderr concurrently to avoid a pipe-buffer
        // deadlock (a full stderr would otherwise block ffmpeg while we read stdout).
        using var pcm = new MemoryStream();
        var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(pcm);
        var errTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(300_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return null;
        }
        try { copyTask.GetAwaiter().GetResult(); _ = errTask.GetAwaiter().GetResult(); }
        catch { return null; }
        proc.WaitForExit(); // let the readers flush

        if (proc.ExitCode != 0) return null;

        int byteLen = (int)pcm.Length;
        if (byteLen < 2) return null;

        // s16le little-endian → float32 / 32768. Windows is little-endian, so a direct
        // reinterpret of the buffer as Int16 is correct and avoids a per-byte shift.
        int n = byteLen / 2;
        var shorts = MemoryMarshal.Cast<byte, short>(pcm.GetBuffer().AsSpan(0, n * 2));
        var samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = shorts[i] / 32768f;
        return samples;
    }
}
