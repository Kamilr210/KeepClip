namespace KeepClip.Tests.Services;

public class ReplayWriterTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-22T12:00:00Z");
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly string _root = TestEnvironment.NewTempDir("replay-writer");
    private readonly string _segments;
    private string Output => Path.Combine(_root, "Game", "Game 2026-10-22 12-00-00.mp4");

    public ReplayWriterTests()
    {
        TestSettings.Reset();
        Settings.SetClipsRoot(_root);
        _segments = Path.Combine(_root, "segments with ' quote");
        Directory.CreateDirectory(_segments);
        Directory.CreateDirectory(Config.TmpDir);
    }

    private string Segment(int index, int bytes = 100)
    {
        var path = Path.Combine(_segments, $"seg_{index:000000}.ts");
        var data = Enumerable.Repeat((byte)index, ((bytes + 187) / 188) * 188).ToArray();
        for (int offset = 0; offset < data.Length; offset += 188)
        {
            new byte[] { 0x47, 0x41, 0x00, (byte)(0x10 | ((offset / 188) & 15)), 0, 0, 1, 0xe0, 0, 178, 0x80, 0, 0 }.CopyTo(data, offset);
        }
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public async Task Empty_buffer_fails_without_starting_ffmpeg()
    {
        var writer = new ReplayWriter((_, _) => throw new InvalidOperationException("Must not encode"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync(_segments, "Game", 15, false, false));
        Assert.False(File.Exists(Output));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Save_uses_recent_completed_segments_and_a_snapshot_of_the_current_one(bool audio, bool hevc)
    {
        for (int i = 0; i < 12; i++) Segment(i);
        var current = Segment(12, 60_000);
        string? list = null;
        string? snapshot = null;
        var writer = new ReplayWriter((args, timeout) =>
        {
            Assert.Equal(120_000, timeout);
            list = args[Array.IndexOf(args, "-i") + 1];
            var lines = File.ReadAllLines(list);
            Assert.Equal(9, lines.Length);
            Assert.DoesNotContain(lines, line => line.Contains("seg_000003.ts"));
            Assert.Contains(lines, line => line.Contains("seg_000004.ts"));
            Assert.Contains("'\\''", lines[0]);
            snapshot = Assert.Single(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
            Assert.Equal(File.ReadAllBytes(current), File.ReadAllBytes(snapshot));
            Assert.DoesNotContain(lines, line => line.Contains("seg_000012.ts"));
            Assert.Equal(audio, args.Contains("0:a:0?"));
            Assert.Equal(hevc, args.Contains("hvc1"));
            Assert.False(File.Exists(Output));
            File.WriteAllBytes(args[^1], new byte[10_001]);
            return Task.FromResult((0, ""));
        }, new Clock());

        var result = await writer.SaveAsync(_segments, "Game", 15, audio, hevc);

        Assert.Equal(Output, result["path"]);
        Assert.Equal(true, result["ok"]);
        Assert.Equal(10_001, new FileInfo(Output).Length);
        Assert.False(File.Exists(list));
        Assert.False(File.Exists(snapshot));
        Assert.True(File.Exists(current));
    }

    [Fact]
    public async Task Existing_recordings_are_preserved_and_the_name_gets_a_suffix()
    {
        Segment(1);
        Segment(2);
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
        File.WriteAllText(Output, "existing recording");
        var writer = new ReplayWriter((args, _) => { File.WriteAllBytes(args[^1], new byte[10_001]); return Task.FromResult((0, "")); }, new Clock());
        var result = await writer.SaveAsync(_segments, "Game", 15, false, false);
        Assert.EndsWith(" (2).mp4", (string)result["path"]!);
        Assert.Equal("existing recording", File.ReadAllText(Output));
    }

    [Theory]
    [InlineData(1, 20_000)]
    [InlineData(0, 100)]
    public async Task Failed_or_truncated_mux_removes_only_temporary_files(int exitCode, int outputSize)
    {
        var completed = Segment(1);
        Segment(2, 60_000);
        string? temporary = null;
        var writer = new ReplayWriter((args, _) => { temporary = args[^1]; File.WriteAllBytes(temporary, new byte[outputSize]); return Task.FromResult((exitCode, new string('x', 300))); }, new Clock());
        await Assert.ThrowsAsync<Exception>(() => writer.SaveAsync(_segments, "Game", 15, false, false));
        Assert.False(File.Exists(temporary));
        Assert.True(File.Exists(completed));
        Assert.False(File.Exists(Output));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_list_*.txt"));
    }

    [Fact]
    public async Task Too_small_current_segment_does_not_count_as_a_valid_recording()
    {
        Segment(1, 100);
        var writer = new ReplayWriter((_, _) => throw new InvalidOperationException("Must not encode"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync(_segments, "Game", 15, false, false));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
    }

    [Fact]
    public async Task A_file_created_while_muxing_is_not_overwritten()
    {
        Segment(1);
        Segment(2);
        var writer = new ReplayWriter((args, _) =>
        {
            File.WriteAllText(Output, "concurrent recording");
            File.WriteAllBytes(args[^1], new byte[10_001]);
            return Task.FromResult((0, ""));
        }, new Clock());
        var result = await writer.SaveAsync(_segments, "Game", 15, false, false);
        Assert.Equal("concurrent recording", File.ReadAllText(Output));
        Assert.NotEqual(Output, result["path"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exception_or_cancellation_during_mux_releases_temporary_files_and_preserves_the_ring(bool cancelled)
    {
        var completed = Segment(1);
        var current = Segment(2, 60_000);
        var original = File.ReadAllBytes(current);
        string? temporary = null;
        var writer = new ReplayWriter((args, _) =>
        {
            temporary = args[^1];
            File.WriteAllBytes(temporary, new byte[10_001]);
            return cancelled ? Task.FromCanceled<(int, string)>(new CancellationToken(true)) : Task.FromException<(int, string)>(new IOException("synthetic encoder interruption"));
        }, new Clock());

        if (cancelled) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.SaveAsync(_segments, "Game", 15, false, false));
        else await Assert.ThrowsAsync<IOException>(() => writer.SaveAsync(_segments, "Game", 15, false, false));

        Assert.False(File.Exists(temporary));
        Assert.False(File.Exists(Output));
        Assert.Equal(original, File.ReadAllBytes(current));
        Assert.True(File.Exists(completed));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_list_*.txt"));
        using var reopened = new FileStream(current, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(original.Length, reopened.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unreadable_current_segment_never_damages_completed_segments(bool hasCompleted)
    {
        var completed = hasCompleted ? Segment(1) : null;
        var current = Segment(2, 60_000);
        int muxCalls = 0;
        var writer = new ReplayWriter((args, _) =>
        {
            muxCalls++;
            var list = args[Array.IndexOf(args, "-i") + 1];
            var entry = Assert.Single(File.ReadAllLines(list));
            Assert.Contains("seg_000001.ts", entry);
            Assert.DoesNotContain("seg_000002.ts", entry);
            File.WriteAllBytes(args[^1], new byte[10_001]);
            return Task.FromResult((0, ""));
        }, new Clock());

        using (var recording = new FileStream(current, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            if (hasCompleted)
            {
                var result = await writer.SaveAsync(_segments, "Game", 15, false, false);
                Assert.Equal(true, result["ok"]);
                Assert.Equal(1, muxCalls);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveAsync(_segments, "Game", 15, false, false));
                Assert.Equal(0, muxCalls);
                Assert.False(File.Exists(Output));
            }
        }

        Assert.Equal(60_160, new FileInfo(current).Length);
        if (completed is not null) Assert.Equal(188, new FileInfo(completed).Length);
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_list_*.txt"));
    }

    [Fact]
    public async Task Current_segment_is_snapshotted_before_mux_so_new_recording_bytes_do_not_change_the_saved_input()
    {
        Segment(1);
        var current = Segment(2, 60_000);
        var original = File.ReadAllBytes(current);
        var muxStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishMux = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new ReplayWriter(async (args, _) =>
        {
            var snapshot = Assert.Single(Directory.GetFiles(Config.TmpDir, "replay_part_*.ts"));
            muxStarted.SetResult(snapshot);
            await finishMux.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(original, File.ReadAllBytes(snapshot));
            File.WriteAllBytes(args[^1], new byte[10_001]);
            return (0, "");
        }, new Clock());

        var saving = writer.SaveAsync(_segments, "Game", 15, false, false);
        var snapshotPath = await muxStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var recording = new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.None);
            await recording.WriteAsync(new byte[] { 9, 8, 7, 6 });
        }
        finally { finishMux.TrySetResult(); }
        var result = await saving.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(true, result["ok"]);
        Assert.Equal(original.Length + 4, new FileInfo(current).Length);
        Assert.False(File.Exists(snapshotPath));
        Assert.Empty(Directory.GetFiles(Config.TmpDir, "replay_list_*.txt"));
    }
}
