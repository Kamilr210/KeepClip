using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KeepClip.Infrastructure;

// Miesza dźwięk z wyjść WASAPI i opcjonalny mikrofon do strumienia PCM dla FFmpeg.
// Pompowanie według zegara ściennego zapobiega zatrzymaniu strumienia podczas ciszy.
internal sealed class AudioPump : IDisposable
{
    // Opóźnienie bufora pochłania krótkie skoki WASAPI bez wstawiania ciszy.
    private const int JitterMs = 200;

    private readonly List<WasapiCapture> _captures = new();
    private readonly NAudio.Wave.SampleProviders.MixingSampleProvider _mixer;
    private Thread? _pumpThread;
    private volatile bool _stop;
    private Stream? _stdin;
    private float[] _fbuf = Array.Empty<float>();
    private byte[] _bbuf = Array.Empty<byte>();
    // Wykrywa poprawny formalnie, lecz całkowicie niemy strumień.
    private float _peak;
    private long _lastPeakLogTicks = DateTime.UtcNow.Ticks;

    public int SampleRate { get; }
    public int Channels { get; }
    public string SampleFormat => "f32le";
    public bool MicActive { get; }

    public static AudioPump? TryCreate(bool withMic, string? outputId, string? inputId)
    {
        try { return new AudioPump(withMic, outputId, inputId); }
        catch { return null; }
    }

    private AudioPump(bool withMic, string? outputId, string? inputId)
    {
        // Bez jawnego wyboru przechwytuje wszystkie aktywne wyjścia, bo dźwięk gry nie
        // zawsze trafia do urządzenia domyślnego. Zegar mieszania nie zależy od WASAPI.
        var en = new MMDeviceEnumerator();
        var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        SampleRate = def.AudioClient.MixFormat.SampleRate;
        Channels = Math.Min(2, Math.Max(1, def.AudioClient.MixFormat.Channels));

        _mixer = new NAudio.Wave.SampleProviders.MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels));

        var names = new List<string>();
        var allOutputs = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        var outputs = outputId is null ? allOutputs : allOutputs.Where(d => d.ID == outputId).ToList();
        if (outputs.Count == 0) outputs = allOutputs;
        foreach (var dev in outputs)
        {
            try
            {
                AddCapture(new WasapiLoopbackCapture(dev), dev.FriendlyName);
                names.Add(dev.FriendlyName);
            }
            catch { }
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

    private void AddCapture(WasapiCapture cap, string name)
    {
        var buf = new BufferedWaveProvider(cap.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
            ReadFully = true,
        };
        cap.DataAvailable += (_, e) =>
        {
            // Nie czyści bufora przy małym opóźnieniu: zaległe próbki są lepsze niż
            // sekundowa dziura, a limit pięciu sekund chroni pamięć przed przepełnieniem.
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

    private void PumpLoop()
    {
        try { PumpLoopCore(); }
        catch (Exception ex)
        {
            // Awaria tej pętli prowadzi do cichych nagrań, więc trafia do diagnostyki.
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

            // Kursor pozostaje JitterMs za zegarem, aby krótkie opóźnienia urządzeń
            // pokrywać prawdziwymi próbkami bez zmiany relacji audio do PTS obrazu.
            long targetFrames = Math.Max(0, sw.ElapsedMilliseconds - JitterMs) * SampleRate / 1000;
            // Twarda resynchronizacja jest potrzebna dopiero po dużej przerwie, np. uśpieniu.
            if (targetFrames - emittedFrames > SampleRate * 30L)
                emittedFrames = targetFrames;
            int frames = (int)Math.Min(targetFrames - emittedFrames, (long)SampleRate);
            if (frames <= 0) continue;

            int samples = frames * Channels;
            if (_fbuf.Length < samples)
            {
                _fbuf = new float[samples * 2];
                _bbuf = new byte[_fbuf.Length * 4];
            }
            int got = _mixer.Read(_fbuf, 0, samples);
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
                return;
            }
        }
    }

    public void Dispose()
    {
        // ReplayService wcześniej kończy FFmpeg, co odblokowuje ewentualny zapis do stdin.
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
