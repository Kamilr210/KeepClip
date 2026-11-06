namespace KeepClip.Tests.Services;

public class ReplayBufferTests
{
    private sealed class FileBackend(ReplayBuffer buffer) : ReplayCaptureBackend
    {
        private bool _running;
        internal int Starts;
        internal override bool Running => _running;
        internal override bool HasBuffer => buffer.HasSegments;
        internal override ReplayStartResult Start(bool freshRing, Func<bool> stillAllowed)
        {
            if (!stillAllowed()) return ReplayStartResult.Deferred;
            Starts++;
            _running = true;
            return ReplayStartResult.Started;
        }
        internal override void Stop() => _running = false;
        internal override void Clear() => buffer.Clear();
    }

    private readonly string _directory = TestEnvironment.NewTempDir("replay-resume");
    private static ReplayCaptureFormat Format => new(ReplayService.TierCpu, "h264_nvenc", 1920, 1080, 60, new("f32le", 48000, 2));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_locked_segment_during_startup_or_shutdown_reports_an_error_and_blocks_capture_until_cleanup_succeeds(bool shutdown)
    {
        var buffer = new ReplayBuffer(_directory);
        var backend = new FileBackend(buffer);
        var controller = new ReplayRecordingController(backend, () => "Counter-Strike 2");
        var path = Path.Combine(_directory, "seg_000001.ts");
        File.WriteAllText(path, "original recording");
        buffer.Accept(Format);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (shutdown) controller.Shutdown();
            else controller.Configure(false, true, "configuration");
            Assert.NotNull(controller.Error);
            Assert.False(controller.Running);
            controller.Configure(true, true, "configuration");
            controller.Tick();
            Assert.False(controller.Running);
            Assert.False(controller.CanSave);
            Assert.Equal(0, backend.Starts);
            Assert.True(File.Exists(path));
        }

        controller.Tick();

        Assert.Null(controller.Error);
        Assert.True(controller.Running);
        Assert.Equal(1, backend.Starts);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_deferred_resume_with_unavailable_audio_preserves_the_original_buffers_audio_and_files()
    {
        var buffer = new ReplayBuffer(_directory);
        Assert.True(buffer.Prepare(true, Format, () => true));
        var path = Path.Combine(_directory, "seg_000001.ts");
        File.WriteAllText(path, "original recording with audio");
        buffer.Accept(Format);

        Assert.False(buffer.Prepare(false, Format with { Audio = null }, () => false));

        Assert.Equal(Format, buffer.Format);
        Assert.Equal(48000, buffer.Format!.Audio!.SampleRate);
        Assert.True(buffer.HasSegments);
        Assert.Equal("original recording with audio", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("audio")]
    [InlineData("sample-rate")]
    [InlineData("channels")]
    [InlineData("size")]
    [InlineData("encoder")]
    [InlineData("capture")]
    public void Changed_runtime_parameters_start_a_clean_ring_instead_of_mixing_incompatible_streams(string change)
    {
        var buffer = new ReplayBuffer(_directory);
        buffer.Accept(Format);
        var path = Path.Combine(_directory, "seg_000001.ts");
        File.WriteAllText(path, "old format");
        var next = change switch
        {
            "audio" => Format with { Audio = null },
            "sample-rate" => Format with { Audio = new("f32le", 44100, 2) },
            "channels" => Format with { Audio = new("f32le", 48000, 1) },
            "size" => Format with { Width = 2560, Height = 1440 },
            "encoder" => Format with { Encoder = "libx264" },
            _ => Format with { Tier = ReplayService.TierGdi },
        };

        Assert.True(buffer.Prepare(false, next, () => true));

        Assert.False(File.Exists(path));
        Assert.Null(buffer.Format);
        Assert.False(buffer.HasSegments);
        buffer.Accept(next);
        File.WriteAllText(path, "new format");
        Assert.Equal(next, buffer.Format);
        Assert.True(buffer.HasSegments);
    }

    [Fact]
    public void A_failed_repair_preserves_the_original_segment_and_does_not_accept_new_metadata()
    {
        var buffer = new ReplayBuffer(_directory);
        var path = Path.Combine(_directory, "seg_000001.ts");
        File.WriteAllText(path, "locked recording");
        buffer.Accept(Format);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => buffer.Prepare(false, Format, () => true));

        Assert.Equal(Format, buffer.Format);
        Assert.Equal("locked recording", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.part"));
    }
}