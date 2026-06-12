using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KeepClip;

/// <summary>
/// Instant-replay buffer — KeepClip's take on ShadowPlay's "Natychmiastowa powtórka".
/// While enabled, one long-lived ffmpeg process captures the desktop (Desktop
/// Duplication via the <c>ddagrab</c> filter, <c>gdigrab</c> as a universal fallback)
/// plus system audio (WASAPI loopback pumped in-process through NAudio into ffmpeg's
/// stdin) into a rolling ring of short mpegts segments under <c>data/tmp/replay</c>.
/// Saving (hotkey or UI) never touches the capture process: completed segments plus a
/// snapshot of the in-progress one are concat-remuxed (<c>-c copy</c>, no re-encode)
/// into an mp4 in the clips root's "Powtórki" subfolder, then a scan registers it.
///
/// mpegts is load-bearing twice over: a partially written .ts decodes up to the last
/// packet (so the freshest seconds make it into the save AND a crash/kill never
/// corrupts the ring), and TS segments concat losslessly with the concat demuxer.
/// </summary>
public static class ReplayService
{
    private const int SegmentSeconds = 4;          // ring granularity; save overshoot ≤ this
    private static readonly object Gate = new();

    private static Process? _ff;
    private static AudioPump? _audio;
    private static Thread? _janitor;
    private static bool _stopRequested;
    private static bool _usingGdigrab;             // true after the ddagrab → gdigrab fallback
    private static bool _audioOn;
    private static string? _encoder;               // probed once per process lifetime
    private static string? _error;                 // last start/runtime failure (PL, shown in UI)
    private static string? _runningSig;            // settings signature the buffer was started with
    private static int _saving;                    // interlocked: a save is in flight
    private static readonly Queue<string> _stderrTail = new();

    private static string SegDir => Path.Combine(Config.TmpDir, "replay");

    /// <summary>
    /// On-screen notification hook (ok, title, subtitle). The desktop shell points
    /// this at the ShadowPlay-style toast so EVERY successful save — hotkey, the
    /// in-app button, or API — confirms visually over the game. Null in server-only.
    /// </summary>
    public static Action<bool, string, string>? Notifier;

    // ---------- settings (persisted via Settings; UI edits them over /api/replay/config) ----------

    public static bool Enabled => Settings.GetString("replay_enabled") == "1";
    public static int DurationS => ClampInt(Settings.GetString("replay_duration_s"), 15, 600, 120);
    public static int Fps => Settings.GetString("replay_fps") == "30" ? 30 : 60;
    /// <summary>"low" | "medium" | "high" (Niska/Średnia/Wysoka in the UI).</summary>
    public static string Quality
    {
        get
        {
            var q = Settings.GetString("replay_quality");
            return q is "low" or "medium" or "high" ? q : "high";
        }
    }
    public static string Hotkey => Settings.GetString("replay_hotkey") ?? "Alt+F10";
    /// <summary>Mix the default microphone into the recording (default on).</summary>
    public static bool MicEnabled => Settings.GetString("replay_mic") != "0";

    private static int ClampInt(string? raw, int min, int max, int fallback)
        => int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;

    private static string SettingsSig() => $"{DurationS}|{Fps}|{Quality}|{MicEnabled}";

    // ---------- public surface ----------

    /// <summary>Status payload for <c>/api/replay/status</c> and the config POST reply.
    /// Deliberately lock-free (worst case a snapshot is a moment stale): the status
    /// endpoint must stay responsive even if a start/stop is wedged inside ffmpeg
    /// or the audio stack.</summary>
    public static object Status()
    {
        var ff = _ff;
        bool running;
        try { running = ff is { HasExited: false }; } catch { running = false; }
        return new Dictionary<string, object?>
        {
            ["enabled"] = Enabled,
            ["running"] = running,
            ["saving"] = _saving != 0,
            ["duration_s"] = DurationS,
            ["fps"] = Fps,
            ["quality"] = Quality,
            ["hotkey"] = Hotkey,
            ["hotkey_active"] = HotkeyManager.IsActive,
            ["encoder"] = running ? _encoder : null,
            ["capture"] = running ? (_usingGdigrab ? "gdigrab" : "ddagrab") : null,
            ["audio"] = running && _audioOn,
            ["mic_enabled"] = MicEnabled,
            ["mic_active"] = running && _audio?.MicActive == true,
            ["error"] = _error,
        };
    }

    /// <summary>
    /// Reconcile the buffer with current settings: start when enabled, stop when
    /// disabled, restart when fps/quality/duration changed. Called at app startup and
    /// after every config POST. Never throws — failures land in <see cref="Status"/>.
    /// </summary>
    public static void ApplyConfig()
    {
        lock (Gate)
        {
            bool running = _ff is { HasExited: false };
            if (!Enabled)
            {
                if (running) StopLocked();
                _error = null;
                return;
            }
            if (running && _runningSig == SettingsSig()) return;
            if (running) StopLocked();
            // Fresh user intent (enable / settings change / app start) clears the
            // crash streak AND the gdigrab fallback — a transient bad spell (game
            // launch storm) must not curse the rest of the process lifetime.
            _consecFails = 0;
            _usingGdigrab = false;
            StartLocked();
        }
    }

    /// <summary>Kill the capture process and audio pump (app shutdown / disable).</summary>
    public static void Shutdown()
    {
        lock (Gate) StopLocked();
    }

    /// <summary>
    /// Distinct audio confirmation: rising two-tone chime = saved, falling = failed.
    /// Sound is the RELIABLE feedback channel — the popup may not composite over
    /// some fullscreen games. Custom cues live in frontend/sounds (user-swappable);
    /// system sounds are the fallback when the files are missing.
    /// </summary>
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
        catch { /* sound is best-effort */ }
    }

    /// <summary>
    /// Dump the last <see cref="DurationS"/> seconds of the ring to an mp4 in
    /// clipsRoot\Powtórki and register it. Throws InvalidOperationException for
    /// user-facing "can't right now" states (buffer off / empty / save in flight).
    /// </summary>
    public static async Task<Dictionary<string, object?>> SaveAsync(string source)
    {
        // Grace window: a game launch/display flip restarts the buffer exactly when
        // the user most wants to save — give the watchdog a moment to bring it back
        // instead of failing the hotkey outright.
        for (int i = 0; Enabled && _ff is not { HasExited: false } && i < 8; i++)
            await Task.Delay(500);
        if (_ff is not { HasExited: false })
            throw new InvalidOperationException("Bufor powtórki nie jest uruchomiony.");
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0)
            throw new InvalidOperationException("Poprzedni zapis powtórki jeszcze trwa.");

        string? listPath = null, snapPath = null, tmpOut = null;
        try
        {
            var files = Directory.GetFiles(SegDir, "seg_*.ts")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                throw new InvalidOperationException("Bufor powtórki jest jeszcze pusty — daj mu kilka sekund.");

            // Highest-numbered segment is the one ffmpeg is writing right now. Snapshot
            // it (TS is decodable mid-write) so the save includes the freshest seconds.
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
            catch { snapPath = null; /* partial unreadable → save completed segments only */ }
            if (snapPath is not null && new FileInfo(snapPath).Length > 50_000)
                take.Add(snapPath);
            if (take.Count == 0)
                throw new InvalidOperationException("Bufor powtórki jest jeszcze pusty — daj mu kilka sekund.");

            // concat demuxer list. Forward slashes + single-quote escaping per its syntax.
            listPath = Path.Combine(Config.TmpDir, $"replay_list_{Guid.NewGuid():N}.txt");
            var sb = new StringBuilder();
            foreach (var f in take)
                sb.Append("file '").Append(f.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            await File.WriteAllTextAsync(listPath, sb.ToString());

            var game = ForegroundGameName() ?? "Pulpit";
            var outDir = Path.Combine(Settings.GetClipsRoot(), Config.ReplaySubdir);
            Directory.CreateDirectory(outDir);
            var outName = $"{game} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4";
            var outPath = Path.Combine(outDir, outName);

            // Remux to a temp file first so the scanner can never see a half-written mp4.
            tmpOut = Path.Combine(Config.TmpDir, $"replay_out_{Guid.NewGuid():N}.mp4");
            var (code, err) = await RunFfmpegAsync(new[]
            {
                "-y", "-f", "concat", "-safe", "0", "-i", listPath,
                "-c", "copy", "-movflags", "+faststart", tmpOut,
            }, timeoutMs: 120_000);
            if (code != 0 || !File.Exists(tmpOut) || new FileInfo(tmpOut).Length < 10_000)
                throw new Exception($"Nie udało się złożyć powtórki (ffmpeg: {Tail(err)})");

            File.Move(tmpOut, outPath, overwrite: true);
            tmpOut = null;

            Scanner.Scan();
            ThumbnailWorker.Ensure();
            PlayCue(ok: true);
            try { Notifier?.Invoke(true, "Powtórka zapisana", outName); } catch { }
            Console.Error.WriteLine($"Powtórka zapisana ({source}): {outPath}");

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

    // ---------- capture process ----------

    private static void StartLocked()
    {
        _error = null;
        _stopRequested = false;
        try
        {
            if (!File.Exists(Config.Ffmpeg))
                throw new Exception("Brak ffmpeg.exe w tools\\bin — uruchom setup.ps1.");

            Directory.CreateDirectory(SegDir);
            foreach (var f in Directory.GetFiles(SegDir)) // stale ring from a previous run
                try { File.Delete(f); } catch { }

            MeasurePrimaryDisplay();
            _encoder ??= ProbeEncoder();

            // Audio is best-effort: a machine with no render device still gets video.
            _audio = AudioPump.TryCreate(MicEnabled);
            _audioOn = _audio is not null;

            _ff = SpawnCapture(_usingGdigrab);
            _audio?.Start(_ff.StandardInput.BaseStream);

            _runningSig = SettingsSig();
            StartWatchdog(_ff);
            StartJanitor();
            Console.Error.WriteLine(
                $"Bufor powtórki: start ({(_usingGdigrab ? "gdigrab" : "ddagrab")}, {_encoder}, " +
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

        // Kill ffmpeg FIRST: that tears down the stdin pipe, which instantly unblocks
        // any audio-thread Write() — disposing NAudio before the pipe reader is gone
        // proved to deadlock StopRecording (capture thread stuck mid-Write → join hangs
        // → Gate held forever → whole API frozen).
        if (_ff is not null)
        {
            try { if (!_ff.HasExited) _ff.Kill(entireProcessTree: true); } catch { }
            // Let it actually die before we sweep the ring below, or its final segment
            // flush re-creates a file right after the delete loop.
            try { _ff.WaitForExit(2000); } catch { }
            try { _ff.Dispose(); } catch { }
            _ff = null;
        }

        // NAudio teardown is best-effort with a hard time cap: a misbehaving audio
        // driver must never wedge stop/disable (the orphaned objects are background
        // threads + COM handles — leaked once, reclaimed at process exit).
        var audio = _audio;
        _audio = null;
        if (audio is not null)
        {
            var t = Task.Run(audio.Dispose);
            if (!t.Wait(TimeSpan.FromSeconds(4)))
                Console.Error.WriteLine("Bufor: porzucam zawieszony teardown audio.");
        }

        _runningSig = null;
        try
        {
            if (Directory.Exists(SegDir))
                foreach (var f in Directory.GetFiles(SegDir)) File.Delete(f);
        }
        catch { }
    }

    private static Process SpawnCapture(bool gdigrab)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            RedirectStandardInput = true,    // audio PCM pipe (unused but harmless without audio)
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var a = psi.ArgumentList;
        void Add(params string[] xs) { foreach (var x in xs) a.Add(x); }

        Add("-hide_banner", "-loglevel", "error");

        if (_audioOn)
        {
            // Plain sample-count timestamps (start at 0). The pump thread already
            // paces writes in wallclock, so sample time ≈ real time. Wallclock
            // stamping (-use_wallclock_as_timestamps) put audio pts at epoch scale
            // while ddagrab video sat near zero — ffmpeg's input scheduler always
            // reads the input that is furthest BEHIND, so it never consumed the
            // "way ahead" audio pipe at all: the pipe filled, the pump blocked, and
            // every recording came out silent.
            Add("-f", _audio!.SampleFormat, "-ar", _audio.SampleRate.ToString(),
                "-ac", _audio.Channels.ToString(),
                "-thread_queue_size", "2048", "-i", "pipe:0");
        }

        // Geometry is PINNED to the primary display's mode measured at start
        // (scale+pad to a fixed even size, ≤3840 wide for NVENC's H.264 4096 limit).
        // Two real-world failures forced this: a game switching display mode makes
        // ddagrab emit different-sized frames mid-stream (encoder dies with EINVAL),
        // and multi-monitor gdigrab grabs the whole virtual desktop (4240 px wide on
        // a 2560+1680 setup → "No capable devices found" from NVENC). gdigrab is
        // therefore also restricted to the primary monitor's region. nv12 because
        // every encoder in the probe chain accepts it, while bgra support varies.
        string norm = $"scale={_capW}:{_capH}:force_original_aspect_ratio=decrease," +
                      $"pad={_capW}:{_capH}:(ow-iw)/2:(oh-ih)/2,format=nv12[v]";
        if (gdigrab)
        {
            Add("-f", "gdigrab", "-framerate", Fps.ToString(), "-thread_queue_size", "128",
                "-offset_x", "0", "-offset_y", "0", "-video_size", $"{_grabW}x{_grabH}",
                "-i", "desktop");
            Add("-filter_complex", $"[{(_audioOn ? 1 : 0)}:v]{norm}");
        }
        else
        {
            Add("-filter_complex",
                $"ddagrab=framerate={Fps}:draw_mouse=1,hwdownload,format=bgra,{norm}");
        }
        Add("-map", "[v]");
        if (_audioOn)
        {
            Add("-map", "0:a");
            Add("-c:a", "aac", "-b:a", "160k");
        }

        // Bitrate ladder (constant disk budget regardless of encoder): the ring holds
        // duration+2 segments, so even Wysoka at 5 min stays under ~1.3 GB of tmp.
        int mbps = Quality switch { "low" => 8, "medium" => 15, _ => 30 };
        string b = $"{mbps}M", maxr = $"{(int)(mbps * 1.5)}M", buf = $"{mbps * 2}M";
        int gop = Fps * SegmentSeconds; // keyframe cadence == segment cadence
        switch (_encoder)
        {
            case "h264_nvenc":
                Add("-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-b:v", b, "-maxrate", maxr, "-bufsize", buf);
                break;
            case "h264_amf":
                Add("-c:v", "h264_amf", "-usage", "transcoding", "-rc", "vbr_peak", "-b:v", b, "-maxrate", maxr);
                break;
            case "h264_qsv":
                Add("-c:v", "h264_qsv", "-preset", "medium", "-b:v", b, "-maxrate", maxr);
                break;
            default:
                Add("-c:v", "libx264", "-preset", "veryfast", "-b:v", b, "-maxrate", maxr, "-bufsize", buf);
                break;
        }
        Add("-g", gop.ToString());

        Add("-f", "segment", "-segment_time", SegmentSeconds.ToString(),
            "-reset_timestamps", "1", "-segment_format", "mpegts",
            Path.Combine(SegDir, "seg_%06d.ts"));

        var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new Exception("ffmpeg nie wystartował.");

        // Drain stderr continuously (avoids pipe-buffer deadlock) into a short tail
        // ring used for diagnostics when the process dies.
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

    /// <summary>
    /// Self-healing on unexpected capture death. Desktop Duplication routinely loses
    /// access mid-run (a game switching display mode, resolution change, UAC/lock
    /// screen) and ddagrab exits with a generic external-library error — so any
    /// unexpected exit schedules a restart instead of leaving the feature dead.
    /// Repeated IMMEDIATE deaths (&lt;10 s uptime) mean ddagrab itself can't run here →
    /// after 2 in a row switch to gdigrab; after 6 in a row give up with the stderr
    /// tail in the status. A run that survives a minute resets the failure streak.
    /// </summary>
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
                if (_consecFails >= 2 && !_usingGdigrab)
                {
                    Console.Error.WriteLine("Bufor: ddagrab pada od razu — przełączam na gdigrab.");
                    _usingGdigrab = true;
                }
                if (_consecFails >= 6)
                {
                    _error = $"Bufor powtórki zatrzymał się: {Tail(tail)}";
                    return;
                }

                int delayMs = _consecFails == 0 ? 2_000 : 3_000 * _consecFails;
                Console.Error.WriteLine(
                    $"Bufor: nieoczekiwany koniec po {uptime.TotalSeconds:F0}s ({Tail(tail)}) — " +
                    $"restart za {delayMs / 1000}s.");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    lock (Gate)
                    {
                        // Settings are the source of truth: the user may have disabled
                        // (or a config POST already restarted) the buffer meanwhile.
                        if (!Enabled || _ff is not null) return;
                        StartLocked();
                    }
                });
            }
        })
        { IsBackground = true, Name = "KeepClip-ReplayWatchdog" };
        t.Start();
    }

    /// <summary>Prune ring segments beyond what the configured duration needs.</summary>
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
                    if (_saving != 0) continue; // never yank files mid-concat
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

    // ---------- helpers ----------

    /// <summary>
    /// One-time pick of the best working H.264 encoder: a 3-frame null encode proves
    /// the driver/GPU actually initializes, not just that ffmpeg compiled it in.
    /// </summary>
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

    // ---- primary display measurement (true pixels, DPI-independent) ----

    private static int _capW = 1920, _capH = 1080;   // encode target (even, ≤3840 wide)
    private static int _grabW = 1920, _grabH = 1080; // gdigrab region (raw primary mode)

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }

    /// <summary>
    /// Read the primary display's CURRENT mode via EnumDisplaySettings — Screen.Bounds
    /// lies under DPI scaling unless the process opted into per-monitor awareness
    /// (which server-only mode never does). Falls back to 1920×1080 if the query fails.
    /// </summary>
    private static void MeasurePrimaryDisplay()
    {
        int w = 0, h = 0;
        try
        {
            var name = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (name is not null && EnumDisplaySettingsW(name, ENUM_CURRENT_SETTINGS, ref dm))
            {
                w = (int)dm.dmPelsWidth;
                h = (int)dm.dmPelsHeight;
            }
        }
        catch { }
        if (w < 320 || h < 240) { w = 1920; h = 1080; }
        _grabW = w; _grabH = h;

        if (w > 3840) { h = (int)Math.Round(h * 3840.0 / w); w = 3840; } // NVENC H.264 cap
        _capW = w & ~1;
        _capH = h & ~1;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    /// <summary>Foreground process name (the game, when saving via hotkey mid-game).
    /// Null for the shell/our own window → caller falls back to "Pulpit".</summary>
    private static string? ForegroundGameName()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (p.Id == Environment.ProcessId) return null;
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return null;
            if (name.Equals("KeepClip", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
        catch { return null; }
    }
}

/// <summary>
/// System audio (WASAPI loopback) optionally MIXED with the default microphone →
/// one float PCM stream into ffmpeg stdin, so saved replays carry game sound and
/// voice together (ShadowPlay-style).
///
/// Loopback is the clock master: every loopback chunk pulls an equal number of
/// samples through a <see cref="MixingSampleProvider"/>, where the mic track (its
/// own buffer, resampled/channel-matched to the loopback format) contributes what
/// it has and silence-pads the rest. NAudio's loopback goes silent-starved when
/// nothing plays, which would stall the whole mux — a muted keepalive stream holds
/// the endpoint active for the session. The mic buffer is hard-capped (~400 ms):
/// clock drift between two devices otherwise accumulates into runaway voice lag.
/// </summary>
internal sealed class AudioPump : IDisposable
{
    private readonly List<WasapiCapture> _captures = new();  // loopbacks of ALL outputs + mic
    private readonly NAudio.Wave.SampleProviders.MixingSampleProvider _mixer;
    private Thread? _pumpThread;
    private volatile bool _stop;
    private Stream? _stdin;
    private float[] _fbuf = Array.Empty<float>();
    private byte[] _bbuf = Array.Empty<byte>();
    // Peak meter: the cheapest possible "is anything actually non-silent" probe.
    // A whole debugging session was lost to an audio track that LOOKED fine
    // (right duration, right format) but was all zeros — never fly blind again.
    private float _peak;
    private long _lastPeakLogTicks = DateTime.UtcNow.Ticks;

    public int SampleRate { get; }
    public int Channels { get; }
    public string SampleFormat => "f32le";   // mixer output is always IEEE float
    public bool MicActive { get; }

    /// <summary>Null when the machine has no render device at all (headless).
    /// A missing/broken microphone or individual output degrades instead of failing.</summary>
    public static AudioPump? TryCreate(bool withMic)
    {
        try { return new AudioPump(withMic); }
        catch { return null; }
    }

    private AudioPump(bool withMic)
    {
        // Two hard-won lessons shape this design:
        //  1. Capture EVERY active output, not just the default — real setups route
        //     sound away from the default device (bug-report machine: default render
        //     was a USB mic's phantom "speakers" while games played through the
        //     monitor's NVIDIA HDMI audio → loopback of the default heard silence).
        //  2. NEVER let a WASAPI capture be the mix clock. Loopback endpoints
        //     deliver nothing while idle (silence keepalives to fix that proved
        //     driver-dependent and silently failed), and a starved master froze the
        //     whole audio stream. The pump below is driven by a wallclock thread:
        //     every track is just a buffer that contributes what its device delivered
        //     and silence-pads the rest. A dead device costs nothing but silence.
        var en = new MMDeviceEnumerator();
        var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        SampleRate = def.AudioClient.MixFormat.SampleRate;
        Channels = Math.Min(2, Math.Max(1, def.AudioClient.MixFormat.Channels));

        _mixer = new NAudio.Wave.SampleProviders.MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels));

        var names = new List<string>();
        foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                AddCapture(new WasapiLoopbackCapture(dev), dev.FriendlyName);
                names.Add(dev.FriendlyName);
            }
            catch { /* this output stays uncaptured; the rest still work */ }
        }
        if (names.Count == 0)
            throw new InvalidOperationException("no capturable render endpoint");

        if (withMic)
        {
            try
            {
                var micName = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).FriendlyName;
                AddCapture(new WasapiCapture(), $"mikrofon {micName}");
                names.Add($"mikrofon: {micName}");
                MicActive = true;
            }
            catch { MicActive = false; }
        }
        Console.Error.WriteLine("Bufor audio: " + string.Join(" + ", names));
    }

    /// <summary>Wire one capture (loopback or mic) into the mixer as a buffered,
    /// format-matched, drift-capped track and remember it for start/dispose.</summary>
    private void AddCapture(WasapiCapture cap, string name)
    {
        var buf = new BufferedWaveProvider(cap.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
            ReadFully = true,    // every track silence-pads when its device is idle/behind
        };
        cap.DataAvailable += (_, e) =>
        {
            buf.AddSamples(e.Buffer, 0, e.BytesRecorded);
            // Drift guard: a device clock running ahead of the pump clock grows its
            // buffer without bound, lagging that track ever further behind the video.
            if (buf.BufferedDuration.TotalMilliseconds > 400)
                buf.ClearBuffer();
        };
        cap.RecordingStopped += (_, e) => Console.Error.WriteLine(
            $"Bufor audio: przechwytywanie '{name}' zatrzymane{(e.Exception is null ? "" : $": {e.Exception.Message}")}");

        ISampleProvider sp = buf.ToSampleProvider();
        if (sp.WaveFormat.SampleRate != SampleRate)
            sp = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(sp, SampleRate);
        if (sp.WaveFormat.Channels == 1 && Channels == 2)
            sp = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels == 2 && Channels == 1)
            sp = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(sp);
        _mixer.AddMixerInput(sp);
        _captures.Add(cap);
    }

    public void Start(Stream ffmpegStdin)
    {
        _stdin = ffmpegStdin;
        foreach (var cap in _captures)
            try { cap.StartRecording(); } catch { }

        _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "KeepClip-AudioPump" };
        _pumpThread.Start();
    }

    /// <summary>Wallclock-paced pump: every ~20 ms emit exactly as many samples as
    /// real time has passed, mixed from whatever the device tracks delivered.</summary>
    private void PumpLoop()
    {
        try { PumpLoopCore(); }
        catch (Exception ex)
        {
            // A dead pump == silent recordings with no other symptom. Make it loud.
            Console.Error.WriteLine($"Bufor audio: POMPA PADŁA: {ex}");
        }
    }

    private void PumpLoopCore()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long emittedFrames = 0;
        while (!_stop)
        {
            Thread.Sleep(20);
            var stdin = _stdin;
            if (stdin is null) return;

            long targetFrames = sw.ElapsedMilliseconds * SampleRate / 1000;
            // Resync after a stall (system sleep, debugger pause): emitting a giant
            // catch-up burst of silence-padded audio helps nobody.
            if (targetFrames - emittedFrames > SampleRate)
                emittedFrames = targetFrames - SampleRate / 10;
            int frames = (int)(targetFrames - emittedFrames);
            if (frames <= 0) continue;

            int samples = frames * Channels;
            if (_fbuf.Length < samples)
            {
                _fbuf = new float[samples * 2];
                _bbuf = new byte[_fbuf.Length * 4];
            }
            int got = _mixer.Read(_fbuf, 0, samples);   // ReadFully inputs ⇒ got == samples
            if (got <= 0) continue;
            emittedFrames += got / Channels;

            for (int i = 0; i < got; i++)
            {
                var a = Math.Abs(_fbuf[i]);
                if (a > _peak) _peak = a;
            }
            var now = DateTime.UtcNow.Ticks;
            if (now - _lastPeakLogTicks > TimeSpan.TicksPerSecond * 15)
            {
                _lastPeakLogTicks = now;
                Console.Error.WriteLine($"Bufor audio: szczyt 15s = {_peak:F4}");
                _peak = 0;
            }

            Buffer.BlockCopy(_fbuf, 0, _bbuf, 0, got * 4);
            try { stdin.Write(_bbuf, 0, got * 4); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Bufor audio: zapis do ffmpeg przerwany ({ex.GetType().Name}) — pompa kończy.");
                return; /* ffmpeg gone; the watchdog owns the restart story */
            }
        }
    }

    public void Dispose()
    {
        // Detach the sink and stop the pump before joining captures — the pump exits
        // on the null sink/flag, and a stdin write blocked on a dead pipe throws once
        // ffmpeg is killed (ReplayService kills it before disposing us).
        _stop = true;
        _stdin = null;
        try { _pumpThread?.Join(2000); } catch { }
        foreach (var cap in _captures)
        {
            try { cap.StopRecording(); } catch { }
            try { cap.Dispose(); } catch { }
        }
    }
}
