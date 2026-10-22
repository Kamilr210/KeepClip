using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeepClip.Tests.Integration;

[Trait("Category", "FFmpeg")]
public class MediaCutFfmpegTests
{
    private sealed class EncoderRunner(bool forceGpuFailure) : MediaProcessRunner
    {
        public List<string> Encoders { get; } = new();

        public override MediaRunResult Run(string exe, IReadOnlyList<string> args, int timeoutSeconds, Action<string>? onOutputLine = null)
        {
            var options = args.ToList();
            var encoder = options[options.IndexOf("-c:v") + 1];
            Encoders.Add(encoder);
            // Rzeczywisty błąd FFmpeg, niezależny od GPU maszyny testowej; kod aplikacji obsługuje ponowienie.
            if (forceGpuFailure && encoder == "h264_nvenc")
                options.InsertRange(options.Count - 1, new[] { "-gpu", "9999" });
            return base.Run(exe, options, timeoutSeconds, onOutputLine);
        }
    }

    private static MediaRunResult Run(string exe, params string[] args)
    {
        Assert.True(File.Exists(exe), $"Brak {exe}. Przygotuj narzędzia przez setup.ps1 przed testami FFmpeg.");
        var result = new MediaProcessRunner().Run(exe, args, 120);
        Assert.False(result.Faulted, result.StdErr);
        Assert.True(result.ExitCode == 0, result.StdErr);
        return result;
    }

    private static string CreateSource(string directory, bool audio, string pixelFormat, int duration)
    {
        var source = Path.Combine(directory, "synthetic source.mp4");
        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", $"testsrc2=size=320x180:rate=10:duration={duration}",
        };
        if (audio)
            args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate=48000:duration={duration}" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", pixelFormat, "-g", "20" });
        if (audio) args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k" });
        args.Add(source);
        Run(Config.Ffmpeg, args.ToArray());
        return source;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [Theory]
    [InlineData(false, true, "yuv420p10le", 20.0)]
    [InlineData(true, true, "yuv420p", 20.0)]
    [InlineData(true, false, "yuv420p", null)]
    public void Application_cut_encodes_a_playable_segment_at_the_reported_timestamps(bool forceGpuFailure, bool audio, string pixelFormat, double? limit)
    {
        var directory = TestEnvironment.NewTempDir("ffmpeg-cut");
        var source = CreateSource(directory, audio, pixelFormat, 305);
        var original = Hash(source);
        var output = Path.Combine(directory, "cut.mp4");
        var runner = new EncoderRunner(forceGpuFailure);

        var result = new MediaProcessor(runner).CutClip(source, output, 171.02, 211.42, limit);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(original, Hash(source));
        if (forceGpuFailure) Assert.Equal(new[] { "h264_nvenc", "libx264" }, runner.Encoders);
        var size = new FileInfo(output).Length;
        Assert.True(size > 0);
        if (limit.HasValue) Assert.True(size <= limit.Value * 1_000_000, $"Wycinek przekracza {limit} MB: {size} bajtów.");
        Assert.Equal(size, result.Stats["size_bytes"]);

        var probe = Run(Config.Ffprobe, "-v", "error", "-count_packets", "-show_streams", "-show_format", "-of", "json", output);
        using var json = JsonDocument.Parse(probe.StdOut);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("yuv420p", video.GetProperty("pix_fmt").GetString());
        Assert.InRange(int.Parse(video.GetProperty("nb_read_packets").GetString()!, CultureInfo.InvariantCulture), 403, 405);
        Assert.Equal(audio ? 2 : 1, streams.Length);
        if (audio)
        {
            var sound = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
            Assert.Equal("aac", sound.GetProperty("codec_name").GetString());
            Assert.True(int.Parse(sound.GetProperty("nb_read_packets").GetString()!, CultureInfo.InvariantCulture) > 0);
        }
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(duration, 40.3, 40.55);
        var decoded = Run(Config.Ffmpeg, "-hide_banner", "-loglevel", "error", "-xerror", "-i", output, "-f", "null", "-");
        Assert.True(string.IsNullOrWhiteSpace(decoded.StdErr), decoded.StdErr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Application_cut_rejects_a_range_without_packets_and_removes_the_output(bool audio)
    {
        var directory = TestEnvironment.NewTempDir("ffmpeg-empty-cut");
        var source = CreateSource(directory, audio, "yuv420p", 2);
        var original = Hash(source);
        var output = Path.Combine(directory, "cut.mp4");

        var result = new MediaProcessor(new EncoderRunner(true)).CutClip(source, output, 3, 5);

        Assert.False(result.Ok);
        Assert.Empty(result.Stats);
        Assert.False(File.Exists(output));
        Assert.Equal(original, Hash(source));
    }

    [Fact]
    public void Application_cut_preserves_the_decoder_error_for_a_broken_input()
    {
        var directory = TestEnvironment.NewTempDir("ffmpeg-broken-cut");
        var source = Path.Combine(directory, "broken.mp4");
        var output = Path.Combine(directory, "cut.mp4");
        File.WriteAllText(source, "synthetic broken MP4");
        var original = Hash(source);

        var result = new MediaProcessor(new MediaProcessRunner()).CutClip(source, output, 0, 1);

        Assert.False(result.Ok);
        Assert.Contains("Invalid data found when processing input", result.Message);
        Assert.False(File.Exists(output));
        Assert.Equal(original, Hash(source));
    }
}