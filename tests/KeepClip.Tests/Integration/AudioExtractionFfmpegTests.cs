namespace KeepClip.Tests.Integration;

[Trait("Category", "FFmpeg")]
public class AudioExtractionFfmpegTests
{
    [Fact]
    public async Task Real_decoder_returns_complete_normalized_audio_and_cleans_up_its_spool()
    {
        var directory = TestEnvironment.NewTempDir("ffmpeg-audio-limit");
        var source = Path.Combine(directory, "synthetic.flac");
        var run = new MediaProcessRunner().Run(Config.Ffmpeg, new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000:duration=2", "-c:a", "flac", source }, 30);
        Assert.False(run.Faulted);
        Assert.True(run.ExitCode == 0, run.StdErr);

        var samples = await AudioExtractor.LoadAsync(source, CancellationToken.None);

        Assert.NotNull(samples);
        Assert.Equal(32000, samples.Length);
        Assert.Contains(samples, sample => sample > 0.01f);
        Assert.All(samples, sample => Assert.InRange(sample, -1, 1));
        Assert.Empty(Directory.EnumerateFiles(Config.TmpDir, "audio-*.pcm"));
    }
}