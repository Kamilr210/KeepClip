using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KeepClip.Infrastructure;

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
    /// <summary>Wallclock lag of the emit cursor — a jitter buffer that lets the buffered
    /// audio absorb WASAPI capture bursts and small device-vs-Stopwatch clock drift over a
    /// replay-length window, instead of silence-padding the gap. Free latency for a
    /// non-live replay buffer, and sync-neutral (see PumpLoopCore).</summary>
    private const int JitterMs = 200;

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
    /// A missing/broken microphone or individual output degrades instead of failing.
    /// <paramref name="outputId"/>/<paramref name="inputId"/> are NAudio MMDevice IDs;
    /// null selects the robust defaults (all outputs / the default mic).</summary>
    public static AudioPump? TryCreate(bool withMic, string? outputId, string? inputId)
    {
        try { return new AudioPump(withMic, outputId, inputId); }
        catch { return null; }
    }

    private AudioPump(bool withMic, string? outputId, string? inputId)
    {
        // Two hard-won lessons shape this design:
        //  1. With NO explicit pick, capture EVERY active output, not just the default —
        //     real setups route sound away from the default device (bug-report machine:
        //     default render was a USB mic's phantom "speakers" while games played
        //     through the monitor's NVIDIA HDMI audio → loopback of the default heard
        //     silence). A user pick narrows it to that one device.
        //  2. NEVER let a WASAPI capture be the mix clock. Loopback endpoints deliver
        //     nothing while idle, which would starve/freeze the stream. The pump below
        //     is a wallclock thread: every track contributes what its device delivered
        //     and silence-pads the rest. A dead device costs nothing but silence.
        var en = new MMDeviceEnumerator();
        var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        SampleRate = def.AudioClient.MixFormat.SampleRate;
        Channels = Math.Min(2, Math.Max(1, def.AudioClient.MixFormat.Channels));

        _mixer = new NAudio.Wave.SampleProviders.MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels));

        var names = new List<string>();
        var allOutputs = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        // A specific selected output, else ALL of them. A vanished selection → all.
        var outputs = outputId is null ? allOutputs : allOutputs.Where(d => d.ID == outputId).ToList();
        if (outputs.Count == 0) outputs = allOutputs;
        foreach (var dev in outputs)
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
                var mic = inputId is not null
                    ? en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                          .FirstOrDefault(d => d.ID == inputId)
                      ?? en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                    : en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                AddCapture(new WasapiCapture(mic), $"mikrofon {mic.FriendlyName}");
                names.Add($"mikrofon: {mic.FriendlyName}");
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
            // No drift-CLEAR here: wiping the buffer on a small backlog (which happened
            // every time the ffmpeg pipe briefly blocked during a capture restart) dropped
            // ~1 s of audio outright — the "audio cuts out for a second" report. The 5 s
            // BufferDuration + DiscardOnBufferOverflow caps memory; the pump drains any
            // backlog (real audio, not silence) and aresample re-syncs it to the video.
            buf.AddSamples(e.Buffer, 0, e.BytesRecorded);
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

            // Emit JitterMs BEHIND wallclock so the buffer keeps a ~JitterMs cushion of REAL
            // audio: a brief WASAPI capture burst/lag (and small device-vs-Stopwatch clock
            // drift accumulated over a replay-length window) is then covered by buffered
            // samples instead of being silence-padded — that padding was the "audio przerywa"
            // cutout. Sync-neutral: every sample maps wall-time→pts the same way (the first
            // emitted sample is the oldest buffered one, ≈ capture start → pts 0), so audio
            // still lines up with the video's pts-0 start; only the buffering latency grows.
            long targetFrames = Math.Max(0, sw.ElapsedMilliseconds - JitterMs) * SampleRate / 1000;
            // Only hard-resync after an EXTREME gap (system sleep): a normal ffmpeg pipe
            // stall is caught up by emitting the REAL backlogged audio over the next few
            // ticks — not by dropping it, which caused the ~1 s audio dropouts.
            if (targetFrames - emittedFrames > SampleRate * 30L)
                emittedFrames = targetFrames;
            // Cap per tick so a backlog drains as a few quick ticks, not one huge alloc/Write.
            int frames = (int)Math.Min(targetFrames - emittedFrames, (long)SampleRate);
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
