using System.Diagnostics;
using System.Runtime.InteropServices;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace KeepClip.Infrastructure;

public static class Transcriber
{
    private const string ModelSetting = "whisper_model";
    private const string ProbeSetting = "whisper_model_probe";
    private const string BlockedSetting = "whisper_model_blocked";

    private static readonly object ModelLock = new();
    private static WhisperFactory? _factory;
    private static string? _activeModel;
    private static bool _probeCleared;

    // Wyznacza dolną granicę drabinki po nieudanej próbie w tej sesji.
    private static int _ladderFloor;

    public static string? ActiveModel { get { lock (ModelLock) return _activeModel; } }

    public readonly record struct Segment(double Start, double End, string Text);

    public static List<Segment> Transcribe(
        string path,
        Action<double, string>? progress = null)
    {
        progress?.Invoke(0.02, "audio");
        var samples = LoadAudioNormalized(path)
            ?? throw new InvalidOperationException($"Nie udało się zdekodować audio: {path}");

        while (true)
        {
            // Filtr wyjątku tylko sprawdza warunek; zwolnienie modelu musi nastąpić po
            // rozwinięciu stosu, gdy procesor korzystający z niego jest już zamknięty.
            try { return RunOnce(samples, progress); }
            catch (Exception ex) when (CanStepDown()) { StepDown(ex); }
        }
    }

    private static List<Segment> RunOnce(float[] samples, Action<double, string>? progress)
    {
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

        // Model przeszedł pełną transkrypcję, więc bufory obliczeniowe też się zmieściły.
        ClearProbe();
        progress?.Invoke(0.97, "saving");
        return outSegs;
    }

    // Jawny wybór użytkownika nigdy nie jest podmieniany, a z ostatniego szczebla nie ma zejścia.
    private static bool CanStepDown()
    {
        lock (ModelLock)
        {
            int current = Array.IndexOf(Config.WhisperModelLadder, _activeModel);
            return Config.WhisperModelOverride is null && current >= 0
                   && current + 1 < Config.WhisperModelLadder.Length;
        }
    }

    private static void StepDown(Exception ex)
    {
        lock (ModelLock)
        {
            int current = Array.IndexOf(Config.WhisperModelLadder, _activeModel);
            if (current < 0) return;

            TranscribeState.PushLog(
                $"Model {_activeModel} nie poradził sobie ({ex.Message}) — przechodzę na lżejszy.");
            _ladderFloor = current + 1;
            _factory?.Dispose();
            _factory = null;
            _activeModel = null;
            ClearProbe();
        }
    }

    private static WhisperFactory GetFactory()
    {
        lock (ModelLock)
        {
            if (_factory is not null) return _factory;

            Exception? last = null;
            foreach (var model in CandidateModels())
            {
                try
                {
                    EnsureModelFile(model);
                    // Znacznik przetrwa twarde zamknięcie procesu przy braku pamięci GPU,
                    // dzięki czemu następny start nie powtórzy tej samej próby.
                    Settings.SetString(ProbeSetting, model);
                    _probeCleared = false;

                    _factory = WhisperFactory.FromPath(Config.WhisperModelPath(model));
                    _activeModel = model;
                    Settings.SetString(ModelSetting, model);
                    ReportLoadedRuntime(model);
                    return _factory;
                }
                catch (Exception ex)
                {
                    last = ex;
                    ClearProbe();
                    TranscribeState.PushLog($"Model {model} nie wystartował: {ex.Message}");
                }
            }
            throw new InvalidOperationException(
                "Nie udało się uruchomić żadnego modelu transkrypcji.", last);
        }
    }

    // Kolejność prób: od modelu pasującego do pamięci GPU w dół.
    private static IEnumerable<string> CandidateModels()
    {
        if (Config.WhisperModelOverride is { } forced) return new[] { forced };

        var ladder = Config.WhisperModelLadder;
        int start = _ladderFloor;

        long vram = GpuInfo.LargestDedicatedVideoMemory();
        while (start < ladder.Length - 1 &&
               Config.WhisperModelMinVram.GetValueOrDefault(ladder[start]) > vram)
            start++;

        // Model, przy którym poprzedni proces zniknął bez śladu, jest pomijany na stałe.
        DetectCrashedModel();
        var blocked = Settings.GetString(BlockedSetting);
        if (blocked is not null)
        {
            int i = Array.IndexOf(ladder, blocked);
            if (i >= 0) start = Math.Max(start, i + 1);
        }

        if (start > 0)
            TranscribeState.PushLog(
                $"Dobór modelu: {GpuInfo.Describe(vram)} → {ladder[Math.Min(start, ladder.Length - 1)]}");
        return ladder.Skip(Math.Min(start, ladder.Length - 1));
    }

    // Pozostawiony znacznik oznacza, że poprzednie uruchomienie nie przeżyło ładowania modelu.
    private static void DetectCrashedModel()
    {
        var probe = Settings.GetString(ProbeSetting);
        if (probe is null) return;
        Settings.SetString(BlockedSetting, probe);
        Settings.SetString(ProbeSetting, "");
        TranscribeState.PushLog(
            $"Model {probe} przerwał poprzednie uruchomienie — zostaje wyłączony na tym komputerze.");
    }

    private static void ClearProbe()
    {
        if (_probeCleared) return;
        _probeCleared = true;
        try { Settings.SetString(ProbeSetting, ""); } catch { }
    }

    // Zapisuje wybrany mechanizm obliczeniowy, aby rozpoznać użycie GPU albo CPU.
    private static void ReportLoadedRuntime(string model)
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
        string msg = $"Silnik STT: {label} [model: {model}]";
        Console.Error.WriteLine(msg);
        TranscribeState.PushLog(msg);
    }

    private static void EnsureModelFile(string model)
    {
        var target = Config.WhisperModelPath(model);
        if (File.Exists(target)) return;
        Directory.CreateDirectory(Config.ModelsDir);

        // Plik .part zapobiega uznaniu przerwanego pobierania za gotowy model.
        var part = target + ".part";
        try
        {
            using (var modelStream = WhisperGgmlDownloader.Default
                       .GetGgmlModelAsync(ResolveGgmlType(model)).GetAwaiter().GetResult())
            using (var file = File.Create(part))
                modelStream.CopyToAsync(file).GetAwaiter().GetResult();
            File.Move(part, target, overwrite: true);
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
