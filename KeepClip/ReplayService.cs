using System.Diagnostics;
using System.Text;
using NAudio.CoreAudioApi;

namespace KeepClip;

// Bufor zapisuje krótkie segmenty MPEG-TS, które można odczytać także podczas zapisu
// i połączyć bez ponownego kodowania obrazu.
public static class ReplayService
{
    private const int SegmentSeconds = 2;
    private static readonly object Gate = new();

    private static Process? _ff;
    private static AudioPump? _audio;
    private static Thread? _janitor;
    private static bool _stopRequested;
    private static bool _audioOn;
    private static string? _encoder;

    // Kolejne poziomy zwiększają zgodność: GPU, transfer przez CPU, a na końcu gdigrab.
    private const int TierGpu = 0, TierCpu = 1, TierGdi = 2;
    private static int _captureTier = -1;
    private static string? _error;
    private static string? _runningSig;
    private static int _saving;
    private static int _segStart;
    private static readonly Queue<string> _stderrTail = new();

    private static string SegDir => Path.Combine(Config.TmpDir, "replay");

    public static bool SaveInProgress => _saving != 0;

    public static Action<bool, string, string>? Notifier;

    public static bool Enabled => Settings.GetString("replay_enabled") == "1";
    public static bool BackgroundEnabled => Settings.GetString("replay_background") == "1";
    public static int DurationS => ClampInt(Settings.GetString("replay_duration_s"), 15, 600, 120);
    public static int Fps =>
        int.TryParse(Settings.GetString("replay_fps"), out var f)
        && f is 30 or 60 or 90 ? f : 60;
    public static string Quality
    {
        get
        {
            var q = Settings.GetString("replay_quality");
            return q is "low" or "medium" or "high" ? q : "high";
        }
    }
    public static string Hotkey => Settings.GetString("replay_hotkey") ?? "Alt+F10";
    public static bool MicEnabled => Settings.GetString("replay_mic") == "1";
    public static string? AudioOutputId => Empty(Settings.GetString("replay_audio_output"));
    public static string? AudioInputId => Empty(Settings.GetString("replay_audio_input"));

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static int ClampInt(string? raw, int min, int max, int fallback)
        => int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;

    private static string SettingsSig()
        => $"{DurationS}|{Fps}|{Quality}|{MicEnabled}|{AudioOutputId}|{AudioInputId}";

    // Celowo bez blokady: punkt stanu ma pozostać dostępny podczas problemów FFmpeg lub audio.
    public static object Status()
    {
        var ff = _ff;
        bool running;
        try { running = ff is { HasExited: false }; } catch { running = false; }
        return new Dictionary<string, object?>
        {
            ["enabled"] = Enabled,
            ["background"] = BackgroundEnabled,
            ["running"] = running,
            ["saving"] = _saving != 0,
            ["duration_s"] = DurationS,
            ["fps"] = Fps,
            ["quality"] = Quality,
            ["hotkey"] = Hotkey,
            ["hotkey_active"] = HotkeyManager.IsActive,
            ["encoder"] = running ? _encoder : null,
            ["capture"] = running ? CaptureName(_captureTier) : null,
            ["audio"] = running && _audioOn,
            ["mic_enabled"] = MicEnabled,
            ["mic_active"] = running && _audio?.MicActive == true,
            ["audio_output"] = AudioOutputId,
            ["audio_input"] = AudioInputId,
            ["error"] = _error,
        };
    }

    public static object AudioDevices()
    {
        var outputs = new List<object>();
        var inputs = new List<object>();
        try
        {
            var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                outputs.Add(new { id = d.ID, name = d.FriendlyName });
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                inputs.Add(new { id = d.ID, name = d.FriendlyName });
        }
        catch { }
        return new { outputs, inputs };
    }

    public static void ApplyConfig()
    {
        lock (Gate)
        {
            bool running = _ff is { HasExited: false };
            if (!Enabled)
            {
                if (running) StopLocked();
                if (_saving == 0) WipeRing();
                _error = null;
                return;
            }
            if (running && _runningSig == SettingsSig()) return;
            if (running) StopLocked();
            // Nowa konfiguracja zeruje eskalację po wcześniejszych awariach przechwytywania.
            _consecFails = 0;
            _captureTier = -1;
            StartLocked(freshRing: true);
        }
    }

    public static void Shutdown()
    {
        lock (Gate) StopLocked();
    }

    // Dźwięk jest odtwarzany od razu, bo nakładka nie zawsze pojawia się nad grą pełnoekranową.
    public static void PlayCue(bool ok)
    {
        try
        {
            var wav = Path.Combine(Config.FrontendDir, "sounds",
                ok ? "replay-saved.wav" : "replay-failed.wav");
            if (File.Exists(wav)) new System.Media.SoundPlayer(wav).Play();
            else if (ok) System.Media.SystemSounds.Asterisk.Play();
            else System.Media.SystemSounds.Hand.Play();
        }
        catch { }
    }

    public static async Task<Dictionary<string, object?>> SaveAsync(string source)
    {
        // Daje mechanizmowi naprawczemu chwilę na restart po zmianie trybu wyświetlania.
        for (int i = 0; Enabled && _ff is not { HasExited: false } && i < 8; i++)
            await Task.Delay(500);
        if (_ff is not { HasExited: false })
            throw new InvalidOperationException("Bufor powtórki nie jest uruchomiony.");
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0)
            throw new InvalidOperationException("Poprzedni zapis powtórki jeszcze trwa.");

        var game = DisplayHelper.ForegroundGameName() ?? "Pulpit";

        // Potwierdza skrót natychmiast, zanim zakończy się składanie dużego pliku.
        PlayCue(ok: true);
        try { Notifier?.Invoke(true, "Zapisywanie powtórki…", "przetwarzanie, chwila…"); } catch { }

        string? listPath = null, snapPath = null, tmpOut = null;
        try
        {
            var files = Directory.GetFiles(SegDir, "seg_*.ts")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                throw new InvalidOperationException("Bufor powtórki jest jeszcze pusty — daj mu kilka sekund.");

            // Kopiuje także aktualnie zapisywany TS, aby zachować najnowsze sekundy.
            var current = files[^1];
            var completed = files.Take(files.Count - 1).ToList();

            int needed = (DurationS + SegmentSeconds - 1) / SegmentSeconds;
            var take = completed.Skip(Math.Max(0, completed.Count - needed)).ToList();

            snapPath = Path.Combine(Config.TmpDir, $"replay_part_{Guid.NewGuid():N}.ts");
            try
            {
                using var src = new FileStream(current, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var dst = File.Create(snapPath);
                await src.CopyToAsync(dst);
            }
            catch { snapPath = null; }
            if (snapPath is not null && new FileInfo(snapPath).Length > 50_000)
                take.Add(snapPath);
            if (take.Count == 0)
                throw new InvalidOperationException("Bufor powtórki jest jeszcze pusty — daj mu kilka sekund.");

        // Demukser łączenia wymaga ukośników i specjalnego zapisu apostrofów.
            listPath = Path.Combine(Config.TmpDir, $"replay_list_{Guid.NewGuid():N}.txt");
            var sb = new StringBuilder();
            foreach (var f in take)
                sb.Append("file '").Append(f.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            await File.WriteAllTextAsync(listPath, sb.ToString());

            var outDir = Path.Combine(Settings.GetClipsRoot(), game);
            Directory.CreateDirectory(outDir);
            foreach (var stale in Directory.GetFiles(outDir, "replay_out_*.part"))
                try { File.Delete(stale); } catch { }
            var outPath = AvailableReplayPath(outDir, $"{game} {DateTime.Now:yyyy-MM-dd HH-mm-ss}");
            var outName = Path.GetFileName(outPath);

            // Obraz jest kopiowany bez utraty jakości, a audio kodowane ponownie, aby naprawić
            // szczeliny między segmentami. Plik tymczasowy leży w folderze docelowym, dzięki
            // czemu końcowe przeniesienie jest szybkim przemianowaniem na tym samym woluminie.
            tmpOut = Path.Combine(outDir, $"replay_out_{Guid.NewGuid():N}.mp4.part");
            bool hevc = _encoder?.StartsWith("hevc", StringComparison.Ordinal) == true;
            var args = new List<string>
            {
                "-y", "-f", "concat", "-safe", "0", "-i", listPath,
                "-map", "0:v:0", "-c:v", "copy",
            };
            if (hevc) { args.Add("-tag:v"); args.Add("hvc1"); }
            if (_audioOn)
                args.AddRange(new[] { "-map", "0:a:0?", "-c:a", "aac", "-b:a", "192k",
                                      "-af", "aresample=async=1:first_pts=0,apad", "-shortest" });
            args.AddRange(new[] { "-f", "mp4", tmpOut });
            var (code, err) = await RunFfmpegAsync(args.ToArray(), timeoutMs: 120_000);
            if (code != 0 || !File.Exists(tmpOut) || new FileInfo(tmpOut).Length < 10_000)
                throw new Exception($"Nie udało się złożyć powtórki (ffmpeg: {Tail(err)})");

            // Antywirus lub indeksator może chwilowo blokować plik tuż po zamknięciu FFmpeg.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmpOut, outPath, overwrite: true); break; }
                catch (IOException) when (attempt < 5) { await Task.Delay(400); }
            }
            tmpOut = null;

            try { Notifier?.Invoke(true, "Powtórka zapisana", outName); } catch { }
            Console.Error.WriteLine($"Powtórka zapisana ({source}): {outPath}");

            Scanner.Scan();
            ThumbnailWorker.Ensure();

            return new Dictionary<string, object?>
            {
                ["ok"] = true, ["file"] = outName, ["game"] = game, ["path"] = outPath,
            };
        }
        finally
        {
            Interlocked.Exchange(ref _saving, 0);
            foreach (var p in new[] { listPath, snapPath, tmpOut })
                try { if (p is not null && File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    private static void StartLocked(bool freshRing)
    {
        _error = null;
        _stopRequested = false;
        try
        {
            if (!File.Exists(Config.Ffmpeg))
                throw new Exception("Brak ffmpeg.exe w tools\\bin — uruchom setup.ps1.");

            Directory.CreateDirectory(SegDir);
            // Restart po awarii zachowuje segmenty i kontynuuje numerację, aby nie tracić bufora.
            if (freshRing && _saving == 0)
            {
                WipeRing();
                _segStart = 0;
            }
            else
            {
                _segStart = NextSegNumber();
            }

            (_grabW, _grabH, _capW, _capH) = DisplayHelper.MeasurePrimary();
            _encoder ??= ProbeEncoder();
            // Ścieżka całkowicie GPU nie domyka segmentów przy statycznym obrazie, więc startuje od CPU.
            if (_captureTier < 0) _captureTier = TierCpu;

            // Brak działającego urządzenia audio nie powinien blokować nagrywania obrazu.
            _audio = AudioPump.TryCreate(MicEnabled, AudioOutputId, AudioInputId);
            _audioOn = _audio is not null;

            _ff = SpawnCapture(_captureTier);
            _audio?.Start(_ff.StandardInput.BaseStream);

            _runningSig = SettingsSig();
            StartWatchdog(_ff);
            StartJanitor();
            Console.Error.WriteLine(
                $"Bufor powtórki: start ({CaptureName(_captureTier)}, {_encoder}, " +
                $"{Fps} fps, {Quality}, {DurationS}s, audio: {(_audioOn ? "tak" : "brak")}, " +
                $"mikrofon: {(_audio?.MicActive == true ? "tak" : "brak")})");
        }
        catch (Exception ex)
        {
            _error = $"Nie udało się uruchomić bufora: {ex.Message}";
            StopLocked();
        }
    }

    private static void StopLocked()
    {
        _stopRequested = true;

        // Najpierw kończy FFmpeg, aby przerwać blokujący zapis audio do stdin i uniknąć zakleszczenia.
        if (_ff is not null)
        {
            try { if (!_ff.HasExited) _ff.Kill(entireProcessTree: true); } catch { }
            // Czeka na końcowy zapis segmentu, zanim inny kod zacznie czyścić bufor.
            try { _ff.WaitForExit(2000); } catch { }
            try { _ff.Dispose(); } catch { }
            _ff = null;
        }

        // Wadliwy sterownik audio nie może bezterminowo blokować wyłączenia bufora.
        var audio = _audio;
        _audio = null;
        if (audio is not null)
        {
            var t = Task.Run(audio.Dispose);
            if (!t.Wait(TimeSpan.FromSeconds(4)))
                Console.Error.WriteLine("Bufor: porzucam zawieszony teardown audio.");
        }

        _runningSig = null;
        // Nie czyści tutaj segmentów, bo mogą być używane przez restart lub trwający zapis.
    }

    private static Process SpawnCapture(int tier)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var a = psi.ArgumentList;
        void Add(params string[] xs) { foreach (var x in xs) a.Add(x); }

        Add("-hide_banner", "-loglevel", "error");

        if (_audioOn)
        {
            // Audio używa czasu próbek od zera. Znaczniki zegara rzeczywistego miały inną skalę niż
            // ddagrab, przez co FFmpeg przestawał czytać potok i nagrania były nieme.
            Add("-f", _audio!.SampleFormat, "-ar", _audio.SampleRate.ToString(),
                "-ac", _audio.Channels.ToString(),
                "-thread_queue_size", "2048", "-i", "pipe:0");
        }

        // Stały, parzysty rozmiar ekranu głównego chroni enkoder przed zmianą trybu gry
        // i limitem szerokości NVENC. Nie normalizuje PTS: ddagrab używa czasu rzeczywistego,
        // który utrzymuje synchronizację z audio także przy zgubionych klatkach.
        string normCpu = $"scale={_capW}:{_capH}:force_original_aspect_ratio=decrease," +
                         $"pad={_capW}:{_capH}:(ow-iw)/2:(oh-ih)/2,format=nv12[v]";
        if (tier == TierGdi)
        {
            Add("-f", "gdigrab", "-framerate", Fps.ToString(), "-thread_queue_size", "128",
                "-offset_x", "0", "-offset_y", "0", "-video_size", $"{_grabW}x{_grabH}",
                "-i", "desktop");
            Add("-filter_complex", $"[{(_audioOn ? 1 : 0)}:v]{normCpu}");
        }
        else if (tier == TierCpu)
        {
            Add("-filter_complex",
                $"ddagrab=framerate={Fps}:draw_mouse=1,hwdownload,format=bgra,{normCpu}");
        }
        else
        {
            Add("-filter_complex",
                $"ddagrab=framerate={Fps}:draw_mouse=1,null[v]");
        }
        Add("-map", "[v]");
        if (_audioOn)
        {
            // Korekcja synchronizacji audio odbywa się dopiero podczas zapisu powtórki.
            Add("-map", "0:a");
            Add("-c:a", "aac", "-b:a", "192k");
        }

        // Stały kwantyzator utrzymuje jakość szybkiego ruchu lepiej niż ograniczona przepływność VBR.
        int qp = Quality switch { "low" => 28, "medium" => 24, _ => 20 };
        int gop = Fps;
        switch (_encoder)
        {
            case "h264_nvenc":
                Add("-c:v", "h264_nvenc", "-preset", "p7", "-rc", "constqp", "-qp", qp.ToString(), "-bf", "2");
                break;
            case "h264_amf":
                Add("-c:v", "h264_amf", "-usage", "transcoding", "-quality", "quality",
                    "-rc", "cqp", "-qp_i", qp.ToString(), "-qp_p", qp.ToString(), "-qp_b", qp.ToString());
                break;
            case "h264_qsv":
                Add("-c:v", "h264_qsv", "-preset", "slow", "-global_quality", qp.ToString(), "-bf", "2");
                break;
            default:
                Add("-c:v", "libx264", "-preset", "veryfast", "-crf", qp.ToString());
                break;
        }
        Add("-g", gop.ToString());
        Add("-f", "segment", "-segment_time", SegmentSeconds.ToString(),
            "-segment_start_number", _segStart.ToString(),
            "-reset_timestamps", "1", "-segment_format", "mpegts",
            Path.Combine(SegDir, "seg_%06d.ts"));

        var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new Exception("ffmpeg nie wystartował.");

        // Opróżnia strumień błędów, aby nie zablokować procesu, i zachowuje końcówkę do diagnostyki.
        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await proc.StandardError.ReadLineAsync()) is not null)
                    lock (_stderrTail)
                    {
                        _stderrTail.Enqueue(line);
                        while (_stderrTail.Count > 20) _stderrTail.Dequeue();
                    }
            }
            catch { }
        });
        return proc;
    }

    // ddagrab może stracić dostęp po zmianie trybu ekranu; kolejne szybkie awarie
    // powodują przejście na zgodniejszą metodę, a po sześciu próbach zatrzymanie bufora.
    private static int _consecFails;

    private static void StartWatchdog(Process proc)
    {
        var startedAt = DateTime.UtcNow;
        var t = new Thread(() =>
        {
            try { proc.WaitForExit(); } catch { return; }
            lock (Gate)
            {
                if (_stopRequested || !ReferenceEquals(proc, _ff)) return;
                var uptime = DateTime.UtcNow - startedAt;
                string tail;
                lock (_stderrTail) tail = string.Join(" | ", _stderrTail);
                StopLocked();

                _consecFails = uptime < TimeSpan.FromSeconds(10) ? _consecFails + 1 : 0;
                if (_consecFails >= 2 && _captureTier < TierGdi)
                {
                    _captureTier++;
                    Console.Error.WriteLine($"Bufor: przechwytywanie pada od razu — schodzę na „{CaptureName(_captureTier)}”.");
                }
                if (_consecFails >= 6)
                {
                    _error = $"Bufor powtórki zatrzymał się: {Tail(tail)}";
                    return;
                }

                // Pierwsza próba jest szybka; kolejne zwiększają opóźnienie.
                int delayMs = _consecFails == 0 ? 700 : 1_500 * _consecFails;
                Console.Error.WriteLine(
                    $"Bufor: nieoczekiwany koniec po {uptime.TotalSeconds:F0}s ({Tail(tail)}) — " +
                    $"restart za {delayMs} ms (bufor zachowany).");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    lock (Gate)
                    {
                        // W międzyczasie użytkownik mógł wyłączyć lub ponownie uruchomić bufor.
                        if (!Enabled || _ff is not null) return;
                        StartLocked(freshRing: false);
                    }
                });
            }
        })
        { IsBackground = true, Name = "KeepClip-ReplayWatchdog" };
        t.Start();
    }

    private static void WipeRing()
    {
        try
        {
            if (Directory.Exists(SegDir))
                foreach (var f in Directory.GetFiles(SegDir, "seg_*.ts"))
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }

    private static int NextSegNumber()
    {
        int max = -1;
        try
        {
            foreach (var f in Directory.GetFiles(SegDir, "seg_*.ts"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.Length > 4 && int.TryParse(name.AsSpan(4), out var n) && n > max) max = n;
            }
        }
        catch { }
        return max + 1;
    }

    private static void StartJanitor()
    {
        if (_janitor is { IsAlive: true }) return;
        _janitor = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(5_000);
                try
                {
                    if (_ff is not { HasExited: false }) continue;
                    if (_saving != 0) continue;
                    int keep = (DurationS + SegmentSeconds - 1) / SegmentSeconds + 2;
                    var files = Directory.GetFiles(SegDir, "seg_*.ts")
                        .OrderBy(f => f, StringComparer.Ordinal)
                        .ToList();
                    foreach (var f in files.Take(Math.Max(0, files.Count - keep)))
                        try { File.Delete(f); } catch { }
                }
                catch { }
            }
        })
        { IsBackground = true, Name = "KeepClip-ReplayJanitor" };
        _janitor.Start();
    }

    private static string CaptureName(int tier) => tier switch
    {
        TierGpu => "ddagrab-gpu", TierGdi => "gdigrab", _ => "ddagrab",
    };

    private static string AvailableReplayPath(string directory, string stem)
    {
        var path = Path.Combine(directory, stem + ".mp4");
        for (int copy = 2; File.Exists(path); copy++)
            path = Path.Combine(directory, $"{stem} ({copy}).mp4");
        return path;
    }

    // Krótki test kodowania sprawdza faktyczne uruchomienie sterownika, nie tylko obecność enkodera.
    private static string ProbeEncoder()
    {
        foreach (var enc in new[] { "h264_nvenc", "h264_amf", "h264_qsv" })
        {
            var (code, _) = RunFfmpegAsync(new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=black:s=320x240:r=30",
                "-frames:v", "3", "-c:v", enc, "-f", "null", "-",
            }, timeoutMs: 15_000).GetAwaiter().GetResult();
            if (code == 0) return enc;
        }
        return "libx264";
    }

    private static async Task<(int code, string stderr)> RunFfmpegAsync(string[] args, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var x in args) psi.ArgumentList.Add(x);
        using var p = new Process { StartInfo = psi };
        if (!p.Start()) return (-1, "ffmpeg nie wystartował");
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return (-1, "timeout");
        }
        return (p.ExitCode, await errTask);
    }

    private static string Tail(string s)
    {
        s = (s ?? "").Trim();
        return s.Length <= 220 ? s : "…" + s[^220..];
    }

    private static int _capW = 1920, _capH = 1080;
    private static int _grabW = 1920, _grabH = 1080;
}
