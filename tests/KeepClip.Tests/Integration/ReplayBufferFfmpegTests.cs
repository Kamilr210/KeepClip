using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeepClip.Tests.Integration;

[Trait("Category", "FFmpeg")]
public class ReplayBufferFfmpegTests
{
    private static MediaRunResult Run(string executable, params string[] args)
    {
        Assert.True(File.Exists(executable), $"Missing native test tool: {executable}");
        var result = new MediaProcessRunner().Run(executable, args, 60);
        Assert.False(result.Faulted, result.StdErr);
        Assert.True(result.ExitCode == 0, result.StdErr);
        return result;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 37, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 37, 0)]
    [InlineData(false, 37, 3)]
    [InlineData(true, 37, 3)]
    public async Task Save_excludes_an_incomplete_live_video_pes_with_b_frames_and_optional_audio(bool audio, int partialTransportBytes, int afterKeyframe)
    {
        TestSettings.Reset();
        var root = TestEnvironment.NewTempDir("replay-live-tail");
        Settings.SetClipsRoot(root);
        Directory.CreateDirectory(Config.TmpDir);
        var ring = Path.Combine(root, "ring");
        Directory.CreateDirectory(ring);
        var args = new List<string> { "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=9" };
        if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", "sine=sample_rate=48000:duration=9", "-c:a", "aac", "-b:a", "128k" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "10", "-g", "30", "-keyint_min", "30", "-sc_threshold", "0", "-bf", "2", "-f", "segment", "-segment_time", "2", "-reset_timestamps", "1", "-segment_format", "mpegts", Path.Combine(ring, "seg_%06d.ts") });
        Run(Config.Ffmpeg, args.ToArray());
        var segments = Directory.GetFiles(ring, "seg_*.ts").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, segments.Length);
        var final = segments[^1];
        using var packetsJson = JsonDocument.Parse(Run(Config.Ffprobe, "-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=pos,size,flags", "-of", "json", final).StdOut);
        var packets = packetsJson.RootElement.GetProperty("packets").EnumerateArray().ToArray();
        Assert.Equal(60, packets.Length);
        int incompleteIndex = Array.FindIndex(packets, 1, packet => packet.GetProperty("flags").GetString()!.Contains('K'));
        Assert.True(incompleteIndex > 0);
        incompleteIndex += afterKeyframe;
        using var completeFrames = JsonDocument.Parse(Run(Config.Ffprobe, "-v", "error", "-select_streams", "v:0", "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", final).StdOut);
        Assert.True(int.Parse(packets[incompleteIndex].GetProperty("size").GetString()!, CultureInfo.InvariantCulture) > 376);
        long cut = long.Parse(packets[incompleteIndex].GetProperty("pos").GetString()!, CultureInfo.InvariantCulture) + 376 + partialTransportBytes;
        Assert.True(cut > 50_000, "The fixture must exercise the live snapshot branch.");
        using (var stream = File.OpenWrite(final)) stream.SetLength(cut);
        var hashes = segments.ToDictionary(path => path, Hash);

        var unsafeList = Path.Combine(root, "unsafe.ffconcat");
        File.WriteAllLines(unsafeList, segments.Select(path => "file '" + path.Replace('\\', '/') + "'"));
        var unsafeClip = Path.Combine(root, "unsafe.mp4");
        Run(Config.Ffmpeg, "-v", "error", "-f", "concat", "-safe", "0", "-i", unsafeList, "-map", "0:v:0", "-c:v", "copy", unsafeClip);
        var broken = new MediaProcessRunner().Run(Config.Ffmpeg, new[] { "-nostdin", "-v", "error", "-xerror", "-err_detect", "explode", "-i", unsafeClip, "-map", "0:v:0", "-f", "null", "-" }, 30);
        Assert.False(broken.Faulted);
        Assert.NotEqual(0, broken.ExitCode);
        Assert.Contains("Invalid data", broken.StdErr);

        var writer = new ReplayWriter((arguments, timeout) => Task.Run(() =>
        {
            var result = new MediaProcessRunner().Run(Config.Ffmpeg, arguments, timeout / 1000);
            Assert.False(result.Faulted, result.StdErr);
            return (result.ExitCode, result.StdErr);
        }));
        Dictionary<string, object?> saved;
        using (var activeRecorder = new FileStream(final, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            saved = await writer.SaveAsync(ring, "Tail regression", 15, audio, false);
        var output = Assert.IsType<string>(saved["path"]);
        using var verified = JsonDocument.Parse(Run(Config.Ffprobe, "-v", "error", "-count_frames", "-show_streams", "-of", "json", output).StdOut);
        var streams = verified.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        int savedFrames = int.Parse(video.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture);
        if (afterKeyframe == 0) Assert.Equal(210 + incompleteIndex, savedFrames);
        else Assert.InRange(savedFrames, 210 + incompleteIndex - 3, 210 + incompleteIndex);
        Assert.Equal(audio ? 2 : 1, streams.Length);
        using var savedFrameJson = JsonDocument.Parse(Run(Config.Ffprobe, "-v", "error", "-select_streams", "v:0", "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", output).StdOut);
        var savedTimes = savedFrameJson.RootElement.GetProperty("frames").EnumerateArray().Select(frame => double.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!, CultureInfo.InvariantCulture)).ToArray();
        var referenceTimes = completeFrames.RootElement.GetProperty("frames").EnumerateArray().Select(frame => double.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!, CultureInfo.InvariantCulture)).ToArray();
        for (int index = 210; index < savedTimes.Length; index++)
            Assert.InRange(Math.Abs((savedTimes[index] - savedTimes[210]) - (referenceTimes[index - 210] - referenceTimes[0])), 0, 0.00002);
        var decoded = Run(Config.Ffmpeg, "-nostdin", "-v", "error", "-xerror", "-err_detect", "explode", "-i", output, "-fps_mode", "passthrough", "-enc_time_base:v", "demux", "-f", "null", "-");
        Assert.True(string.IsNullOrWhiteSpace(decoded.StdErr), decoded.StdErr);
        foreach (var segment in segments) Assert.Equal(hashes[segment], Hash(segment));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_list_*.txt"));
    }

    private static void RecordGdiSamples(string ring, bool audio)
    {
        var options = new ReplayCaptureOptions(ReplayService.TierGdi, 60, "high", "libx264", 640, 360, 640, 360, 0, ring, audio ? new("f32le", 48000, 2) : null);
        var args = ReplayCaptureCommand.Create(options).ArgumentList.ToList();
        // Use the real GDI output pipeline with 32 samples/s from a synthetic source advertising 60 fps.
        int firstInput = args.IndexOf("-f");
        args.RemoveRange(firstInput, args.IndexOf("-filter_complex") - firstInput);
        var inputs = new List<string>();
        if (audio) inputs.AddRange(new[] { "-f", "lavfi", "-i", "sine=sample_rate=48000:duration=8" });
        inputs.AddRange(new[] { "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=60:duration=8,select='lt(mod(n*32,60),32)',settb=AVTB" });
        args.InsertRange(firstInput, inputs);
        var captured = Run(Config.Ffmpeg, args.ToArray());
        var progress = captured.StdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("progress=end", progress[^1]);
        Assert.Equal("dup_frames=0", progress.Last(line => line.StartsWith("dup_frames=", StringComparison.Ordinal)));
        Assert.Equal("drop_frames=0", progress.Last(line => line.StartsWith("drop_frames=", StringComparison.Ordinal)));
    }

    private static void RecordConstantRateSamples(string ring, bool audio)
    {
        var input = new List<string>
        {
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=8",
        };
        if (audio) input.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=8" });
        input.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", "-g", "60", "-keyint_min", "60", "-sc_threshold", "0" });
        if (audio) input.AddRange(new[] { "-c:a", "aac", "-b:a", "128k", "-shortest" });
        input.AddRange(new[] { "-f", "segment", "-segment_time", ReplayService.SegmentSeconds.ToString(), "-reset_timestamps", "1", "-segment_format", "mpegts", Path.Combine(ring, "seg_%06d.ts") });
        Run(Config.Ffmpeg, input.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Recorded_segments_are_saved_as_a_complete_playable_clip_without_modifying_the_buffer(bool audio, bool slowerGdiSource)
    {
        TestSettings.Reset();
        var root = TestEnvironment.NewTempDir("replay-buffer-ffmpeg");
        Settings.SetClipsRoot(root);
        Directory.CreateDirectory(Config.TmpDir);
        var ring = Path.Combine(root, "synthetic ring");
        Directory.CreateDirectory(ring);
        if (slowerGdiSource) RecordGdiSamples(ring, audio);
        else RecordConstantRateSamples(ring, audio);
        var segments = Directory.GetFiles(ring, "seg_*.ts").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, segments.Length);
        if (slowerGdiSource)
        {
            foreach (var segment in segments)
            {
                var durationText = Run(Config.Ffprobe, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", segment).StdOut;
                Assert.InRange(double.Parse(durationText, CultureInfo.InvariantCulture), 1.9, 2.1);
            }
        }
        Assert.True(new FileInfo(segments[^1]).Length > 50_000, "Synthetic current segment must exercise the production snapshot path.");
        var original = segments.ToDictionary(path => path, Hash);
        string? snapshot = null, list = null, temporary = null;
        var writer = new ReplayWriter(async (args, timeout) =>
        {
            list = args[Array.IndexOf(args, "-i") + 1];
            temporary = args[^1];
            snapshot = Assert.Single(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
            Assert.Equal(original[segments[^1]], Hash(snapshot));
            var result = await Task.Run(() => new MediaProcessRunner().Run(Config.Ffmpeg, args, (int)Math.Ceiling(timeout / 1000.0)));
            Assert.False(result.Faulted, result.StdErr);
            return (result.ExitCode, result.StdErr);
        });

        var saved = await writer.SaveAsync(ring, "Synthetic test", 15, audio, false);

        Assert.Equal(true, saved["ok"]);
        var output = Assert.IsType<string>(saved["path"]);
        Assert.Equal(Path.Combine(root, "Synthetic test"), Path.GetDirectoryName(output));
        Assert.True(new FileInfo(output).Length > 10_000);
        Assert.Equal(segments, Directory.GetFiles(ring, "seg_*.ts").Order(StringComparer.Ordinal));
        foreach (var segment in segments) Assert.Equal(original[segment], Hash(segment));
        Assert.False(File.Exists(snapshot));
        Assert.False(File.Exists(list));
        Assert.False(File.Exists(temporary));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, "*.part"));

        var probe = Run(Config.Ffprobe, "-v", "error", "-count_frames", "-show_packets", "-show_streams", "-show_format", "-of", "json", output);
        using var json = JsonDocument.Parse(probe.StdOut);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal(640, video.GetProperty("width").GetInt32());
        Assert.Equal(360, video.GetProperty("height").GetInt32());
        int expectedFrames = slowerGdiSource ? 256 : 240;
        Assert.Equal(expectedFrames, int.Parse(video.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture));
        var packets = json.RootElement.GetProperty("packets").EnumerateArray().Where(packet => packet.GetProperty("stream_index").GetInt32() == video.GetProperty("index").GetInt32()).ToArray();
        Assert.Equal(expectedFrames, packets.Length);
        for (int index = 1; index < packets.Length; index++)
        {
            Assert.True(packets[index].GetProperty("pts").GetInt64() > packets[index - 1].GetProperty("pts").GetInt64(), $"Video PTS must increase at packet {index}.");
            Assert.True(packets[index].GetProperty("dts").GetInt64() > packets[index - 1].GetProperty("dts").GetInt64(), $"Video DTS must increase at packet {index}.");
        }
        Assert.Equal(audio ? 2 : 1, streams.Length);
        if (audio)
        {
            var sound = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
            Assert.Equal("aac", sound.GetProperty("codec_name").GetString());
            Assert.True(int.Parse(sound.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture) > 0);
            if (slowerGdiSource)
            {
                double videoDuration = double.Parse(video.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                double audioDuration = double.Parse(sound.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                Assert.InRange(Math.Abs(videoDuration - audioDuration), 0, 0.1);
            }
        }
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(duration, 7.9, 8.3);
        var decoded = Run(Config.Ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-i", output, "-fps_mode", "passthrough", "-enc_time_base", "demux", "-f", "null", "-");
        Assert.True(string.IsNullOrWhiteSpace(decoded.StdErr), decoded.StdErr);
    }
}