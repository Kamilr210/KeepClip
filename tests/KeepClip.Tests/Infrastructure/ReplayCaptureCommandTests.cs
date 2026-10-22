namespace KeepClip.Tests.Infrastructure;

public class ReplayCaptureCommandTests
{
    private static ReplayCaptureOptions Options(int tier = ReplayService.TierCpu, string? encoder = "libx264", string quality = "high", ReplayAudioFormat? audio = null)
        => new(tier, 60, quality, encoder, 2560, 1440, 1920, 1080, 42, Path.Combine(TestEnvironment.DataDir, "ring with spaces"), audio);
    private static string Value(IList<string> args, string flag) => args[args.IndexOf(flag) + 1];

    [Theory]
    [InlineData("h264_nvenc", "-qp", "p7")]
    [InlineData("h264_amf", "-qp_i", null)]
    [InlineData("h264_qsv", "-global_quality", "slow")]
    [InlineData("libx264", "-crf", "veryfast")]
    [InlineData(null, "-crf", "veryfast")]
    public void Encoders_receive_their_supported_quality_options(string? encoder, string qualityFlag, string? preset)
    {
        var command = ReplayCaptureCommand.Create(Options(encoder: encoder));
        var args = command.ArgumentList;
        Assert.Equal(encoder ?? "libx264", Value(args, "-c:v"));
        Assert.Equal("20", Value(args, qualityFlag));
        if (preset is not null) Assert.Equal(preset, Value(args, "-preset"));
        Assert.Equal(Config.Ffmpeg, command.FileName);
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
        Assert.Equal("60", Value(args, "-g"));
        Assert.Equal("2", Value(args, "-segment_time"));
        Assert.Equal("42", Value(args, "-segment_start_number"));
        Assert.Equal(Path.Combine(Options().SegmentDirectory, "seg_%06d.ts"), args[^1]);
    }

    [Theory]
    [InlineData("low", "28")]
    [InlineData("medium", "24")]
    [InlineData("high", "20")]
    public void Quality_presets_produce_the_expected_quantization(string quality, string expected)
        => Assert.Equal(expected, Value(ReplayCaptureCommand.Create(Options(quality: quality)).ArgumentList, "-crf"));

    [Theory]
    [InlineData(ReplayService.TierCpu, "hwdownload")]
    [InlineData(ReplayService.TierGpu, "null[v]")]
    [InlineData(ReplayService.TierGdi, "[0:v]scale=1920:1080")]
    public void Capture_tiers_use_the_expected_video_source_and_filters(int tier, string filter)
    {
        var args = ReplayCaptureCommand.Create(Options(tier)).ArgumentList;
        Assert.Contains(filter, Value(args, "-filter_complex"));
        if (tier == ReplayService.TierGdi)
        {
            Assert.Contains("gdigrab", args);
            Assert.Equal("2560x1440", Value(args, "-video_size"));
            Assert.Contains("desktop", args);
        }
        else Assert.Contains("ddagrab=framerate=60", Value(args, "-filter_complex"));
        Assert.DoesNotContain("0:a", args);
    }

    [Fact]
    public void Audio_pipe_is_mapped_separately_from_gdi_video()
    {
        var args = ReplayCaptureCommand.Create(Options(ReplayService.TierGdi, audio: new("f32le", 48000, 2))).ArgumentList;
        Assert.Contains("f32le", args);
        Assert.Equal("48000", Value(args, "-ar"));
        Assert.Equal("2", Value(args, "-ac"));
        Assert.Contains("pipe:0", args);
        Assert.Contains("0:a", args);
        Assert.Equal("aac", Value(args, "-c:a"));
        Assert.StartsWith("[1:v]", Value(args, "-filter_complex"));
    }
}