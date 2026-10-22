using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace KeepClip;

public static class ReplayService
{
    internal const int SegmentSeconds = 2;
    private static readonly object Gate = new();

    private static Process? _ff;
    private static AudioPump? _audio;
    private static Thread? _janitor;
    private static bool _stopRequested;
    private static bool _audioOn;
    private static string? _encoder;

    internal const int TierGpu = 0, TierCpu = 1, TierGdi = 2;
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

            _consecFails = 0;
            _captureTier = -1;
            StartLocked(freshRing: true);
        }
    }

    public static void Shutdown()
    {
        lock (Gate) StopLocked();
    }

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
        for (int i = 0; Enabled && _ff is not { HasExited: false } && i < 8; i++)
            await Task.Delay(500);
        if (_ff is not { HasExited: false })
            throw new InvalidOperationException(Strings.Get("replay.notRunning"));
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0)
            throw new InvalidOperationException(Strings.Get("replay.saveInProgress"));

        var game = DisplayHelper.ForegroundGameName() ?? "Pulpit";

        PlayCue(ok: true);
        try { Notifier?.Invoke(true, Strings.Get("replay.savingTitle"), Strings.Get("replay.savingSubtitle")); } catch { }

        try
        {
            var writer = new ReplayWriter(RunFfmpegAsync);
            var result = await writer.SaveAsync(SegDir, game, DurationS, _audioOn, _encoder?.StartsWith("hevc", StringComparison.Ordinal) == true);
            try { Notifier?.Invoke(true, Strings.Get("replay.savedTitle"), (string)result["file"]!); } catch { }
            await Console.Error.WriteLineAsync($"Powtórka zapisana ({source}): {result["path"]}");
            Scanner.Scan();
            ThumbnailWorker.Ensure();
            return result;
        }
        finally { Interlocked.Exchange(ref _saving, 0); }
    }

    private static void StartLocked(bool freshRing)
    {
        _error = null;
        _stopRequested = false;
        try
        {
            if (!File.Exists(Config.Ffmpeg))
                throw new Exception(Strings.Get("replay.ffmpegMissing"));

            Directory.CreateDirectory(SegDir);

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

            if (_captureTier < 0)
                _captureTier = DisplayHelper.PreferGdiCapture() ? TierGdi : TierCpu;

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

        if (_ff is not null)
        {
            try { if (!_ff.HasExited) _ff.Kill(entireProcessTree: true); } catch { }

            try { _ff.WaitForExit(2000); } catch { }
            try { _ff.Dispose(); } catch { }
            _ff = null;
        }

        var audio = _audio;
        _audio = null;
        if (audio is not null)
        {
            var t = Task.Run(audio.Dispose);
            if (!t.Wait(TimeSpan.FromSeconds(4)))
                Console.Error.WriteLine("Bufor: porzucam zawieszony teardown audio.");
        }

        _runningSig = null;
    }

    private static Process SpawnCapture(int tier)
    {
        var audio = _audioOn && _audio is not null ? new ReplayAudioFormat(_audio.SampleFormat, _audio.SampleRate, _audio.Channels) : null;
        var psi = ReplayCaptureCommand.Create(new(tier, Fps, Quality, _encoder, _grabW, _grabH, _capW, _capH, _segStart, SegDir, audio));

        var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new Exception(Strings.Get("replay.ffmpegStart"));

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

    private static int _consecFails;

    private static void StartWatchdog(Process proc)
    {
        var startedAt = DateTime.UtcNow;
        var t = new Thread(() =>
        {
            try { proc.WaitForExit(); } catch { return; }

            try
            {
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

                    int delayMs = _consecFails == 0 ? 700 : 1_500 * _consecFails;
                    Console.Error.WriteLine(
                        $"Bufor: nieoczekiwany koniec po {uptime.TotalSeconds:F0}s ({Tail(tail)}) — " +
                        $"restart za {delayMs} ms (bufor zachowany).");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(delayMs);
                        lock (Gate)
                        {
                            if (!Enabled || _ff is not null) return;
                            StartLocked(freshRing: false);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("watchdog bufora powtórki", ex);
                lock (Gate) _error = $"Bufor powtórki zatrzymał się: {ex.Message}";
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

    internal static string Tail(string s)
    {
        s = (s ?? "").Trim();
        return s.Length <= 220 ? s : "…" + s[^220..];
    }

    private static int _capW = 1920, _capH = 1080;
    private static int _grabW = 1920, _grabH = 1080;
}
