using System.Security.Cryptography;
using System.Text.Json;

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
        var command = ReplayCaptureCommand.Create(Options(tier));
        var args = command.ArgumentList;
        Assert.Contains(filter, Value(args, "-filter_complex"));
        if (tier == ReplayService.TierGdi)
        {
            Assert.Contains("gdigrab", args);
            Assert.Equal("2560x1440", Value(args, "-video_size"));
            Assert.Equal("0", Value(args, "-use_captureblt"));
            Assert.Equal("1", Value(args, "-high_precision"));
            Assert.Equal("autobsf", Value(args, "-fflags"));
            Assert.True(args.IndexOf("-fflags") < args.IndexOf("desktop"));
            Assert.Contains("desktop", args);
            Assert.True(command.RedirectStandardOutput);
            Assert.Equal("pipe:1", Value(args, "-progress"));
            Assert.Equal("2", Value(args, "-stats_period"));
        }
        else
        {
            Assert.Contains("ddagrab=framerate=60", Value(args, "-filter_complex"));
            Assert.DoesNotContain("-use_captureblt", args);
            Assert.DoesNotContain("-high_precision", args);
            Assert.DoesNotContain("-fflags", args);
            Assert.False(command.RedirectStandardOutput);
            Assert.DoesNotContain("-progress", args);
            Assert.DoesNotContain("-fps_mode:v", args);
            Assert.DoesNotContain("-enc_time_base:v", args);
            Assert.DoesNotContain("-force_key_frames:v", args);
        }
        Assert.DoesNotContain("0:a", args);
    }

    [Theory]
    [InlineData("libx264", "-forced-idr")]
    [InlineData("h264_nvenc", "-forced-idr")]
    [InlineData("h264_amf", "-forced_idr")]
    [InlineData("h264_qsv", "-forced_idr")]
    public void Gdi_encoders_preserve_sample_times_and_start_segments_on_independent_frames(string encoder, string idrOption)
    {
        var args = ReplayCaptureCommand.Create(Options(ReplayService.TierGdi, encoder)).ArgumentList;
        Assert.Equal("passthrough", Value(args, "-fps_mode:v"));
        Assert.Equal("filter", Value(args, "-enc_time_base:v"));
        Assert.Equal($"expr:gte(t,n_forced*{ReplayService.SegmentSeconds})", Value(args, "-force_key_frames:v"));
        Assert.Equal("1", Value(args, idrOption));
        Assert.Single(args, arg => arg == "-bf");
        Assert.Equal("0", Value(args, "-bf"));
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
        Assert.Equal("autobsf", Value(args, "-fflags"));
        Assert.True(args.IndexOf("-fflags") > args.IndexOf("pipe:0"));
        Assert.True(args.IndexOf("-fflags") < args.IndexOf("desktop"));
    }

    [Theory]
    [InlineData(ReplayService.TierCpu, 30)]
    [InlineData(ReplayService.TierCpu, 90)]
    [InlineData(ReplayService.TierGdi, 30)]
    [InlineData(ReplayService.TierGdi, 90)]
    public void Capture_rate_and_keyframe_interval_follow_the_selected_fps_with_audio_enabled(int tier, int fps)
    {
        var options = Options(tier, audio: new("f32le", 48000, 2)) with { Fps = fps };
        var args = ReplayCaptureCommand.Create(options).ArgumentList;

        Assert.Equal(fps.ToString(), Value(args, "-g"));
        Assert.Equal("2", Value(args, "-segment_time"));
        Assert.Equal("192k", Value(args, "-b:a"));
        if (tier == ReplayService.TierGdi)
        {
            Assert.Equal(fps.ToString(), Value(args, "-framerate"));
            Assert.Equal("0", Value(args, "-draw_mouse"));
            Assert.Equal("0", Value(args, "-use_captureblt"));
            Assert.Equal("1", Value(args, "-high_precision"));
            Assert.Equal("autobsf", Value(args, "-fflags"));
            Assert.StartsWith("[1:v]", Value(args, "-filter_complex"));
        }
        else Assert.Contains($"ddagrab=framerate={fps}:draw_mouse=0", Value(args, "-filter_complex"));
        Assert.Contains("0:a", args);
    }

    [Fact]
    public void Encoding_manifest_tracks_real_capture_commands_for_supported_benchmark_profiles()
    {
        var profiles = new[] { (Name: "1080p30", Width: 1920, Height: 1080, Fps: 30), (Name: "1080p60", Width: 1920, Height: 1080, Fps: 60), (Name: "1440p60", Width: 2560, Height: 1440, Fps: 60) }.Select(profile => new
        {
            name = profile.Name, width = profile.Width, height = profile.Height, fps = profile.Fps, quality = "high",
            encoders = new[] { "libx264", "h264_nvenc", "h264_amf", "h264_qsv" }.Select(encoder =>
            {
                var options = Options(encoder: encoder) with { Fps = profile.Fps, GrabWidth = profile.Width, GrabHeight = profile.Height, CaptureWidth = profile.Width, CaptureHeight = profile.Height };
                var args = ReplayCaptureCommand.Create(options).ArgumentList.ToArray();
                int start = Array.IndexOf(args, "-c:v");
                int end = Array.IndexOf(args, "-g") + 2;
                Assert.True(start >= 0 && end > start);
                Assert.Equal(encoder, args[start + 1]);
                Assert.Equal(profile.Fps.ToString(), args[end - 1]);
                Assert.Equal("2", args[Array.IndexOf(args, "-segment_time") + 1]);
                var filter = args[Array.IndexOf(args, "-filter_complex") + 1];
                Assert.Contains($"scale={profile.Width}:{profile.Height}:", filter);
                Assert.Contains("format=nv12[v]", filter);
                return new { name = encoder, arguments = args[start..end], normalizationFilter = filter[filter.IndexOf("scale=", StringComparison.Ordinal)..] };
            }).ToArray(),
        }).ToArray();

        if (Environment.GetEnvironmentVariable("KEEPCLIP_EXPORT_RECORDING_COMMANDS") != "1") return;
        var source = Path.Combine(Config.AppRoot, "KeepClip", "Infrastructure", "ReplayCaptureCommand.cs");
        var manifest = new { schemaVersion = 1, generatedUtc = DateTimeOffset.UtcNow, sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), profiles };
        var directory = Path.Combine(Config.AppRoot, "TestResults", "recording");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "command-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}
