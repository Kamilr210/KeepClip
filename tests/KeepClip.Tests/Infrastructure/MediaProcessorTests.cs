using System.Globalization;

namespace KeepClip.Tests.Infrastructure;

public class MediaProcessorTests
{
    private sealed class Runner(Func<string, IReadOnlyList<string>, int, Action<string>?, MediaRunResult> run) : MediaProcessRunner
    {
        public override MediaRunResult Run(string exe, IReadOnlyList<string> args, int timeoutSeconds, Action<string>? onOutputLine = null) => run(exe, args, timeoutSeconds, onOutputLine);
    }

    private static MediaRunResult Success(string stdout = "") => new(false, 0, stdout, "");
    private static string Value(IReadOnlyList<string> args, string option) => args[args.ToList().IndexOf(option) + 1];
    private static string TempFile(string name = "source.mp4")
    {
        var file = Path.Combine(TestEnvironment.NewTempDir("media"), name);
        File.WriteAllBytes(file, new byte[16]);
        return file;
    }

    [Theory]
    [InlineData("{\"format\":{\"duration\":\"12.5\"}}", 12.5)]
    [InlineData("{\"format\":{\"duration\":5}}", 5.0)]
    [InlineData("{\"format\":{}}", null)]
    [InlineData("invalid json", null)]
    public void Probe_duration_reads_numeric_and_string_durations_independently_of_locale(string json, double? expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            var processor = new MediaProcessor(new Runner((exe, args, timeout, _) =>
            {
                Assert.Equal(Config.Ffprobe, exe);
                Assert.Equal("source with spaces.mp4", args[^1]);
                Assert.Equal(30, timeout);
                return Success(json);
            }));
            Assert.Equal(expected, processor.ProbeDuration("source with spaces.mp4"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData(true, -1)]
    [InlineData(false, 1)]
    public void Probe_failure_never_returns_fabricated_media_information(bool faulted, int exitCode)
    {
        var processor = new MediaProcessor(new Runner((_, _, _, _) => new(faulted, exitCode, "{}", "failure")));
        Assert.Null(processor.ProbeDuration("a.mp4"));
        Assert.Null(processor.ProbePlaybackVideo("a.mp4"));
    }

    [Theory]
    [InlineData("60000/1001", "30/1", 59.94005994005994)]
    [InlineData("0/0", "60/1", 60.0)]
    [InlineData("invalid", "90", 90.0)]
    [InlineData("", "0/0", 0.0)]
    [InlineData("30/no", "25", 25.0)]
    public void Video_probe_handles_fractional_rates_and_falls_back_when_average_rate_is_unknown(string average, string raw, double expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { streams = new[] { new { codec_name = "h264", width = 1920, height = 1080, avg_frame_rate = average, r_frame_rate = raw } } });
        var processor = new MediaProcessor(new Runner((_, _, _, _) => Success(json)));
        var info = processor.ProbePlaybackVideo("a.mp4");
        Assert.NotNull(info);
        Assert.Equal(expected, info.Value.Fps, 8);
        Assert.Equal("h264", info.Value.Codec);
        Assert.Equal(1920, info.Value.Width);
    }

    [Fact]
    public void Playback_proxy_falls_back_to_cpu_and_publishes_only_a_complete_file()
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "proxy.mp4");
        int encodes = 0;
        var progress = new List<double>();
        var processor = new MediaProcessor(new Runner((exe, args, _, callback) =>
        {
            if (exe == Config.Ffprobe) return Success("{\"streams\":[{\"width\":1281,\"height\":720,\"avg_frame_rate\":\"240\"}]}");
            encodes++;
            Assert.False(File.Exists(output));
            Assert.NotEqual(output, args[^1]);
            if (encodes == 1)
            {
                Assert.Contains("h264_nvenc", args);
                Assert.Contains("scale_cuda=1280:-2", args);
                File.WriteAllText(args[^1], "partial");
                return new(false, 1, "", "GPU unavailable");
            }
            Assert.Contains("libx264", args);
            Assert.False(File.Exists(args[^1]));
            callback!("out_time_us=5000000");
            callback("out_time=00:00:08.000");
            File.WriteAllBytes(args[^1], new byte[2048]);
            return Success();
        }));

        var result = processor.CreatePlaybackProxy(source, output, 10, progress.Add);

        Assert.True(result.Ok);
        Assert.Equal(2, encodes);
        Assert.Equal(2048, new FileInfo(output).Length);
        Assert.Equal(new[] { 0.5, 0.8, 1.0 }, progress);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(source)!, "*.tmp.mp4"));
    }

    [Fact]
    public void Failed_proxy_discards_partial_outputs_and_reports_the_last_encoder_error()
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "proxy.mp4");
        var processor = new MediaProcessor(new Runner((exe, args, _, _) =>
        {
            if (exe == Config.Ffprobe) return Success("{}");
            File.WriteAllText(args[^1], "partial");
            return new(false, 1, "", "first line\nlast error\n");
        }));
        var result = processor.CreatePlaybackProxy(source, output, 0);
        Assert.False(result.Ok);
        Assert.Contains("last error", result.Message);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(source)!, "*.tmp.mp4"));
    }

    [Fact]
    public void Repair_retries_with_a_skip_and_returns_the_amount_trimmed()
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "fixed.mp4");
        int attempts = 0;
        var processor = new MediaProcessor(new Runner((exe, args, _, _) =>
        {
            if (exe == Config.Ffprobe) return Success();
            attempts++;
            if (attempts == 1) return new(false, 1, "", "broken start");
            Assert.Equal("1.00", Value(args, "-ss"));
            File.WriteAllBytes(args[^1], new byte[1024 * 1024 + 1]);
            return Success();
        }));
        var result = processor.FixBrokenClip(source, output);
        Assert.True(result.Ok);
        Assert.Equal(1, result.Trimmed);
        Assert.Equal(output, result.Output);
        Assert.Equal(2, attempts);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Exhausted_repair_removes_its_temporary_file_and_keeps_the_source()
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "fixed.mp4");
        int attempts = 0;
        var processor = new MediaProcessor(new Runner((_, args, _, _) => { attempts++; File.WriteAllText(args[^1], "partial"); return new(false, 1, "", "broken"); }));
        var result = processor.FixBrokenClip(source, output);
        Assert.False(result.Ok);
        Assert.Null(result.Output);
        Assert.Equal(8, attempts);
        Assert.False(File.Exists(output));
        Assert.True(File.Exists(source));
    }

    [Theory]
    [InlineData(true, "", 999)]
    [InlineData(false, "", 0)]
    [InlineData(false, "bad frame\nbad frame\n", 2)]
    public void Decode_probe_counts_errors_and_distinguishes_failed_execution(bool faulted, string stderr, int expected)
    {
        var processor = new MediaProcessor(new Runner((_, args, _, _) => { Assert.Contains("%+3.0", args); return new(faulted, faulted ? -1 : 0, "", stderr); }));
        Assert.Equal(expected, processor.ProbeDecodeErrors("test.mp4", 3));
    }

    [Fact]
    public void Cut_reduces_bitrate_after_an_oversized_result_and_reports_statistics()
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "cut.mp4");
        var bitrates = new List<int>();
        var stages = new List<string>();
        var processor = new MediaProcessor(new Runner((exe, args, timeout, callback) =>
        {
            Assert.Equal(Config.Ffmpeg, exe);
            Assert.Equal(600, timeout);
            Assert.Equal("1.000", Value(args, "-ss"));
            Assert.Equal("11.000", Value(args, "-to"));
            bitrates.Add(int.Parse(Value(args, "-b:v").TrimEnd('k'), CultureInfo.InvariantCulture));
            callback!("out_time_us=5000000");
            callback("out_time=00:00:10.000");
            File.WriteAllBytes(output, new byte[bitrates.Count == 1 ? 2_000_000 : 900_000]);
            return Success();
        }));
        var result = processor.CutClip(source, output, 1, 11, 1, (_, stage) => stages.Add(stage));
        Assert.True(result.Ok);
        Assert.Equal(2, bitrates.Count);
        Assert.True(bitrates[1] < bitrates[0]);
        Assert.Contains("cuttingRetry", stages);
        Assert.Equal("saving", stages[^1]);
        Assert.Equal(10.0, result.Stats["duration_s"]);
        Assert.Equal(900_000L, result.Stats["size_bytes"]);
    }

    [Fact]
    public void Cut_without_size_limit_uses_quality_mode_and_keeps_progress_bounded()
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "cut.mp4");
        var progress = new List<double>();
        var processor = new MediaProcessor(new Runner((_, args, _, callback) =>
        {
            Assert.Equal("19", Value(args, "-cq"));
            callback!("unrelated"); callback("out_time_us=-1"); callback("out_time_us=5000000000");
            File.WriteAllBytes(output, new byte[1000]);
            return Success();
        }));
        Assert.True(processor.CutClip(source, output, 0, 5, null, (value, _) => progress.Add(value)).Ok);
        Assert.All(progress, value => Assert.InRange(value, 0, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_cut_removes_partial_output(bool faulted)
    {
        var source = TempFile();
        var output = Path.Combine(Path.GetDirectoryName(source)!, "cut.mp4");
        var processor = new MediaProcessor(new Runner((_, _, _, _) => { File.WriteAllText(output, "partial"); return new(faulted, 1, "", "encoder failure"); }));
        Assert.False(processor.CutClip(source, output, 0, 5).Ok);
        Assert.False(File.Exists(output));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Impossible_size_is_rejected_without_starting_ffmpeg()
    {
        var source = TempFile();
        var processor = new MediaProcessor(new Runner((_, _, _, _) => throw new InvalidOperationException("Must not encode")));
        Assert.False(processor.CutClip(source, Path.Combine(Path.GetDirectoryName(source)!, "cut.mp4"), 0, 30, 0.01).Ok);
    }

    [Fact]
    public void Thumbnail_uses_the_midpoint_for_short_videos()
    {
        var id = 9_900_001L;
        Directory.CreateDirectory(Config.ThumbsDir);
        var processor = new MediaProcessor(new Runner((exe, args, _, _) =>
        {
            if (exe == Config.Ffprobe) return Success("{\"format\":{\"duration\":4}}");
            Assert.Equal("2.00", Value(args, "-ss"));
            File.WriteAllBytes(args[^1], new byte[] { 1 });
            return Success();
        }));
        try { Assert.True(processor.MakeThumbnail(id, "short.mp4")); }
        finally { File.Delete(Config.ThumbPath(id)); }
    }
}