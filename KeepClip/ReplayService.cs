using System.Diagnostics;
using System.Text;
using NAudio.CoreAudioApi;

namespace KeepClip;

/// <summary>
/// Instant-replay buffer — KeepClip's take on ShadowPlay's "Natychmiastowa powtórka".
/// While enabled, one long-lived ffmpeg process captures the desktop (Desktop
/// Duplication via the <c>ddagrab</c> filter, with a <c>gdigrab</c> fallback) plus
/// system audio (WASAPI loopback pumped in-process through NAudio into ffmpeg's stdin)
/// into a rolling ring of short H.264 mpegts segments under <c>data/tmp/replay</c>.
/// Saving (hotkey or UI) never touches the capture process: completed segments plus a
/// snapshot of the in-progress one are concat-remuxed (video copied verbatim, audio
/// re-encoded to heal segment-seam gaps) into an mp4 in the clips root's "Powtórki"
/// subfolder, then a scan registers it.
///
/// mpegts is load-bearing twice over: a partially written .ts decodes up to the last
/// packet (so the freshest seconds make it into the save AND a crash/kill never
/// corrupts the ring), and TS segments concat losslessly with the concat demuxer.
/// </summary>
public static class ReplayService
{
    private const int SegmentSeconds = 2;          // ring granularity; save overshoot ≤ this
    private static readonly object Gate = new();

    private static Process? _ff;
    private static AudioPump? _audio;
    private static Thread? _janitor;
    private static bool _stopRequested;
    private static bool _audioOn;
    private static string? _encoder;               // probed once per process lifetime

    // Capture tiers, tried in order and escalated by the watchdog on repeated immediate
    // deaths. Tier 0 keeps frames on the GPU end-to-end (ddagrab D3D11 frames → NVENC,
    // no hwdownload/scale/pad) and is only valid for h264-class NVENC (here hevc_nvenc);
    // tier 1 is the universal CPU round-trip (ddagrab → hwdownload → scale/pad → encoder);
    // tier 2 is gdigrab for hosts where ddagrab can't run at all.
    private const int TierGpu = 0, TierCpu = 1, TierGdi = 2;
    private static int _captureTier = -1;          // -1 = decide from the encoder at next start
    private static string? _error;                 // last start/runtime failure (PL, shown in UI)
    private static string? _runningSig;            // settings signature the buffer was started with
    private static int _saving;                    // interlocked: a save is in flight
    private static int _segStart;                  // next segment number (continues across crash-restarts)
    private static readonly Queue<string> _stderrTail = new();

    private static string SegDir => Path.Combine(Config.TmpDir, "replay");

    /// <summary>True while a save is being assembled. Lets the hotkey handler treat a
    /// re-press as a no-op (not a failure cue) and stops a crash-restart from wiping the
    /// ring out from under the in-flight save.</summary>
    public static bool SaveInProgress => _saving != 0;

    /// <summary>
    /// On-screen notification hook (ok, title, subtitle). The desktop shell points
    /// this at the ShadowPlay-style toast so EVERY successful save — hotkey, the
    /// in-app button, or API — confirms visually over the game. Null in server-only.
    /// </summary>
    public static Action<bool, string, string>? Notifier;

    // ---------- settings (persisted via Settings; UI edits them over /api/replay/config) ----------

    public static bool Enabled => Settings.GetString("replay_enabled") == "1";
    /// <summary>Background mode: closing the window hides the app to the system tray
    /// (recording keeps running) instead of exiting. Only meaningful while
    /// <see cref="Enabled"/> — with the buffer off there is nothing to keep alive.</summary>
    public static bool BackgroundEnabled => Settings.GetString("replay_background") == "1";
    public static int DurationS => ClampInt(Settings.GetString("replay_duration_s"), 15, 600, 120);
    /// <summary>Capture frame rate (60 default). Capped at 90: measured real content tops
    /// out at ~113 unique fps (DWM composition ceiling for Desktop Duplication in
    /// borderless), so higher settings only inflated files (165 fps ≈ +27% size) and
    /// strained players. Anything off the allowed list falls back to 60.</summary>
    public static int Fps =>
        int.TryParse(Settings.GetString("replay_fps"), out var f)
        && f is 30 or 60 or 90 ? f : 60;
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
    /// <summary>Mix a microphone into the recording. Default OFF (opt-in): a background
    /// replay buffer must not silently grab the mic on every launch.</summary>
    public static bool MicEnabled => Settings.GetString("replay_mic") == "1";
    /// <summary>Render device to capture (WASAPI loopback), by NAudio MMDevice ID.
    /// Null/empty = capture ALL active outputs (robust when the default device is a
    /// silent phantom — exactly the dev's own machine).</summary>
    public static string? AudioOutputId => Empty(Settings.GetString("replay_audio_output"));
    /// <summary>Microphone device by MMDevice ID. Null/empty = the default capture device.</summary>
    public static string? AudioInputId => Empty(Settings.GetString("replay_audio_input"));

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static int ClampInt(string? raw, int min, int max, int fallback)
        => int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;

    private static string SettingsSig()
        => $"{DurationS}|{Fps}|{Quality}|{MicEnabled}|{AudioOutputId}|{AudioInputId}";

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

    /// <summary>
    /// Active render (outputs) + capture (inputs) endpoints for the settings dropdowns.
    /// IDs are NAudio MMDevice IDs — exactly what <see cref="AudioOutputId"/> /
    /// <see cref="AudioInputId"/> match on. Backs GET /api/replay/audio-devices.
    /// </summary>
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
        catch { /* no audio stack → empty lists; UI falls back to "default" only */ }
        return new { outputs, inputs };
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
                if (_saving == 0) WipeRing();   // clean stop → clear the ring
                _error = null;
                return;
            }
            if (running && _runningSig == SettingsSig()) return;
            if (running) StopLocked();
            // Fresh user intent (enable / settings change / app start) clears the
            // crash streak AND the capture-tier escalation — a transient bad spell (game
            // launch storm) must not curse the rest of the process lifetime.
            _consecFails = 0;
            _captureTier = -1;
            StartLocked(freshRing: true);
        }
    }

    /// <summary>Kill the capture process and audio pump (app shutdown / disable).</summary>
    public static void Shutdown()
    {
        lock (Gate) StopLocked();
    }

    /// <summary>
    /// Distinct audio feedback: the KeepClip chime = save fired, falling tone = failed.
    /// Sound is the RELIABLE feedback channel — the popup may not composite over
    /// some fullscreen games. Custom cues live in frontend/sounds (user-swappable);
    /// system sounds are the fallback when the files are missing.
    /// The chime plays IMMEDIATELY when the save starts (user decision: instant KeepClip
    /// sound on the press, silence on success, only a failure is voiced afterwards).
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

        // Immediate feedback on BOTH channels: assembling a multi-hundred-MB file takes
        // seconds, and without an instant signal the user can't tell mid-game whether the
        // press registered. The KeepClip chime plays RIGHT HERE (not after assembly) —
        // success stays silent afterwards; only a failure is voiced later.
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

            var game = DisplayHelper.ForegroundGameName() ?? "Pulpit";
            var outDir = Path.Combine(Settings.GetClipsRoot(), Config.ReplaySubdir);
            Directory.CreateDirectory(outDir);
            // Sweep temp orphans from earlier failed saves (best-effort; a held file stays).
            foreach (var stale in Directory.GetFiles(outDir, "replay_out_*.part"))
                try { File.Delete(stale); } catch { }
            var outName = $"{game} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4";
            var outPath = Path.Combine(outDir, outName);

            // LOSSLESS-VIDEO REMUX (ShadowPlay-style). The freshest `needed` segments are
            // already selected above, so we COPY the captured video stream verbatim — no
            // decode, no re-encode — and the saved replay keeps exactly the quality we
            // captured (the old double-encode visibly softened it). Audio is re-encoded with
            // aresample=async=1 to heal the sub-frame gaps at the TS segment seams (the seam
            // drift was always audio, not video); apad + -shortest tie the audio length to
            // the copied video so they stay in sync at any buffer fill. Stream copy starts on
            // the oldest segment's keyframe, so the clip can run up to ~SegmentSeconds longer
            // than DurationS — a harmless bit of extra lead-in for a replay. HEVC in mp4 needs
            // the hvc1 tag to play in the in-app player and most other players.
            //
            // I/O is the whole save cost (hundreds of MB at 90 fps), so the temp file lives
            // IN THE DESTINATION FOLDER: the final File.Move is a same-volume rename instead
            // of a cross-drive copy (data/tmp is on C:, the library typically isn't). The
            // ".part" extension keeps the scanner from registering it mid-write. No
            // +faststart: it rewrites the entire file once more just to front-load the moov
            // atom, and both the in-app player (HTTP range) and Drive playback handle
            // end-of-file moov fine — dropping it roughly halves the save time.
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
            args.AddRange(new[] { "-f", "mp4", tmpOut });   // .part wouldn't infer the muxer
            var (code, err) = await RunFfmpegAsync(args.ToArray(), timeoutMs: 120_000);
            if (code != 0 || !File.Exists(tmpOut) || new FileInfo(tmpOut).Length < 10_000)
                throw new Exception($"Nie udało się złożyć powtórki (ffmpeg: {Tail(err)})");

            // Same volume → instant rename. Retried: right after ffmpeg closes the file,
            // AV/search-indexer/Explorer-thumbnailer often briefly hold it open on the
            // library drive, and a single rename then fails with a sharing violation
            // (seen in the wild: "file is being used by another process").
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(tmpOut, outPath, overwrite: true); break; }
                catch (IOException) when (attempt < 5) { await Task.Delay(400); }
            }
            tmpOut = null;

            // Success is confirmed visually only (the chime already played at save start);
            // the library scan below is internal bookkeeping.
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

    // ---------- capture process ----------

    private static void StartLocked(bool freshRing)
    {
        _error = null;
        _stopRequested = false;
        try
        {
            if (!File.Exists(Config.Ffmpeg))
                throw new Exception("Brak ffmpeg.exe w tools\\bin — uruchom setup.ps1.");

            Directory.CreateDirectory(SegDir);
            // Fresh start (enable / settings change) clears the ring and numbers from 0.
            // A crash-restart (freshRing=false) — or a fresh start while a save is reading
            // the ring — PRESERVES it and continues numbering, so the buffer survives the
            // frequent ddagrab DXGI access-loss crashes: the recording stays continuous,
            // losing only the ~1 s restart gap instead of the whole buffered replay.
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
            // Start on the CPU round-trip (tier 1). GPU-native (tier 0) is DISABLED for now:
            // on real ddagrab D3D11 frames its ring stays near-empty — the segments never
            // complete (most likely -fps_mode cfr can't duplicate hardware frames to fill the
            // rate when the desktop is static, so PTS barely advances), even though the encode
            // + segment muxer + setpts are correct on CPU/synthetic sources. Tier 0 stays in
            // SpawnCapture, ready to re-enable once that's fixed; the watchdog still escalates
            // tier 1 → gdigrab. On a strong GPU at 1440p the CPU round-trip isn't a bottleneck.
            if (_captureTier < 0) _captureTier = TierCpu;

            // Audio is best-effort: a machine with no render device still gets video.
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
        // No ring wipe here: the ring must survive both a crash-restart (so footage isn't
        // lost) and an in-flight save (which is reading these files). Wiping happens only
        // on a clean enable/disable, via WipeRing() in ApplyConfig / StartLocked.
    }

    private static Process SpawnCapture(int tier)
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
        // This pinning applies to the CPU round-trip (tier 1) and gdigrab (tier 2). The
        // GPU-native path (tier 0) keeps ddagrab's D3D11 frames untouched — no scale/pad,
        // no hwdownload — and lets NVENC do the BGRA→NV12 conversion on the GPU, so there
        // is no per-frame GPU→CPU→GPU copy; the trade is no geometry pinning (a mid-stream
        // mode change can crash the encoder → the watchdog restarts and re-measures).
        //
        // NO pts normalization: ddagrab stamps frames with wallclock µs pts and the muxer
        // trusts them — exactly the arrangement of the last known-good build. A previous
        // attempt normalized with `setpts=N/{Fps}` to smooth irregular PTS, but setpts works
        // in TIMEBASE UNITS (µs here), not seconds — N/60 collapsed every pts to ~0 µs, so
        // `-segment_time` never elapsed, the first segment never closed, and every save
        // reported an empty buffer ("bufor pusty"). The correct spelling would be
        // N/({Fps}*TB), but N-based pts also desync from realtime audio whenever ddagrab
        // drops frames under GPU load, so wallclock pts are the right call anyway.
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
        else   // TierGpu: D3D11 frames straight into NVENC, no hwdownload/scale/pad
        {
            Add("-filter_complex",
                $"ddagrab=framerate={Fps}:draw_mouse=1,null[v]");
        }
        Add("-map", "[v]");
        if (_audioOn)
        {
            // Audio mapped straight from the pipe (sample-count pts), like the known-good
            // build. Sync healing happens at SAVE time (aresample there), not in capture.
            Add("-map", "0:a");
            Add("-c:a", "aac", "-b:a", "192k");
        }

        // Quality-based encoding (constant quantizer) instead of a VBR bitrate cap: VBR
        // starved fast-motion CS2 frames (blocking/blur), constant-QP holds quality steady
        // regardless of motion — ShadowPlay-style. Lower QP = higher quality. p7 + B-frames
        // squeeze the most out of NVENC. Disk: CQP is variable-bitrate, but the ring only
        // ever holds ~duration seconds, so worst-case high-motion still fits in tmp.
        int qp = Quality switch { "low" => 28, "medium" => 24, _ => 20 };
        int gop = Fps;   // 1 s keyframe cadence: better seeking + clean 2 s segment cuts
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
        // No -fps_mode: default vsync passes ddagrab's wallclock pts through untouched,
        // which is what keeps the segment timeline (and A/V sync) real-time correct.

        Add("-f", "segment", "-segment_time", SegmentSeconds.ToString(),
            "-segment_start_number", _segStart.ToString(),   // continue numbering across restarts
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
                // Escalate the capture tier on repeated immediate deaths: GPU-native →
                // CPU round-trip → gdigrab. Each step is more compatible (and slower).
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

                // Quick first retry (DXGI access usually re-acquires instantly); back off
                // only if it keeps dying fast. The buffer is preserved across the restart.
                int delayMs = _consecFails == 0 ? 700 : 1_500 * _consecFails;
                Console.Error.WriteLine(
                    $"Bufor: nieoczekiwany koniec po {uptime.TotalSeconds:F0}s ({Tail(tail)}) — " +
                    $"restart za {delayMs} ms (bufor zachowany).");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    lock (Gate)
                    {
                        // Settings are the source of truth: the user may have disabled
                        // (or a config POST already restarted) the buffer meanwhile.
                        if (!Enabled || _ff is not null) return;
                        StartLocked(freshRing: false);   // keep the buffered footage
                    }
                });
            }
        })
        { IsBackground = true, Name = "KeepClip-ReplayWatchdog" };
        t.Start();
    }

    /// <summary>Delete the rolling segment files (clean enable/disable only — never on a
    /// crash-restart or during a save, where the footage must survive).</summary>
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

    /// <summary>Highest existing segment number + 1, so a restarted ffmpeg continues the
    /// numbering instead of overwriting the preserved ring from 0.</summary>
    private static int NextSegNumber()
    {
        int max = -1;
        try
        {
            foreach (var f in Directory.GetFiles(SegDir, "seg_*.ts"))
            {
                var name = Path.GetFileNameWithoutExtension(f);   // "seg_000123"
                if (name.Length > 4 && int.TryParse(name.AsSpan(4), out var n) && n > max) max = n;
            }
        }
        catch { }
        return max + 1;
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

    private static string CaptureName(int tier) => tier switch
    {
        TierGpu => "ddagrab-gpu", TierGdi => "gdigrab", _ => "ddagrab",
    };

    /// <summary>
    /// One-time pick of the best working H.264 encoder: a 3-frame null encode proves the
    /// driver/GPU actually initializes, not just that ffmpeg compiled it in.
    /// NOTE: HEVC (hevc_nvenc) was tried but crashed the live ddagrab capture after a few
    /// frames on the dev's RTX 5080 (rings filled with 3-frame segments), while h264_nvenc
    /// sustained 60 fps cleanly — so capture stays on H.264 until that's diagnosed with a
    /// running ddagrab. See [[replay-capture-engine-direction]] memory.
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

    // ---- primary display geometry (measured via DisplayHelper at each start) ----

    private static int _capW = 1920, _capH = 1080;   // encode target (even, ≤3840 wide)
    private static int _grabW = 1920, _grabH = 1080; // gdigrab region (raw primary mode)
}
