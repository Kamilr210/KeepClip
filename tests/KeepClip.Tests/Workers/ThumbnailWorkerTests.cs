namespace KeepClip.Tests.Workers;

public class ThumbnailWorkerTests
{
    private readonly string _root = TestEnvironment.NewTempDir("thumbnail-worker");
    private readonly FakeClipOperations _operations = new() { Duration = _ => 10, Thumbnail = (_, _) => true };

    public ThumbnailWorkerTests() => TestDb.Reset();

    private long Clip(string name)
    {
        File.WriteAllText(Path.Combine(_root, name), "synthetic");
        return TestDb.AddClip(name: name, dir: _root);
    }

    [Fact]
    public async Task Missing_first_file_does_not_prevent_processing_later_clips()
    {
        var missing = TestDb.AddClip(name: "missing.mp4", dir: _root);
        var valid = Clip("valid.mp4");
        ThumbnailWorker.Ensure(_operations);
        await ThumbnailWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, new ClipRepository().GetById(missing)!.HasThumb);
        Assert.Equal(1, new ClipRepository().GetById(valid)!.HasThumb);
        Assert.Equal(10, new ClipRepository().GetById(valid)!.Duration);
    }

    [Fact]
    public async Task Failed_duration_probe_is_attempted_once_and_does_not_starve_other_clips()
    {
        var bad = Clip("bad.mp4");
        var good = Clip("good.mp4");
        var probes = new List<string>();
        _operations.Duration = path => { probes.Add(path); return path.EndsWith("bad.mp4") ? null : 12; };
        _operations.Thumbnail = (id, _) => id == good;
        ThumbnailWorker.Ensure(_operations);
        await ThumbnailWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, probes.Count);
        Assert.Equal(2, new ClipRepository().GetById(bad)!.HasThumb);
        Assert.Equal(1, new ClipRepository().GetById(good)!.HasThumb);
        Assert.Equal(12, new ClipRepository().GetById(good)!.Duration);
    }

    [Fact]
    public async Task Existing_thumbnail_is_reused_and_duplicate_start_is_ignored()
    {
        var id = Clip("existing.mp4");
        Directory.CreateDirectory(Config.ThumbsDir);
        File.WriteAllText(Config.ThumbPath(id), "existing thumbnail");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int probes = 0;
        _operations.Duration = _ => { Interlocked.Increment(ref probes); entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return 15; };
        _operations.Thumbnail = (_, _) => throw new InvalidOperationException("Must use existing thumbnail");
        ThumbnailWorker.Ensure(_operations);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var first = ThumbnailWorker.Completion;
            ThumbnailWorker.Ensure(_operations);
            Assert.Same(first, ThumbnailWorker.Completion);
        }
        finally { release.Set(); await ThumbnailWorker.Completion!; File.Delete(Config.ThumbPath(id)); }
        Assert.Equal(1, probes);
        Assert.Equal(1, new ClipRepository().GetById(id)!.HasThumb);
    }
}