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
    public async Task Background_and_interactive_requests_share_one_thumbnail()
    {
        var id = Clip("shared.mp4");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        _operations.Thumbnail = (clipId, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            Directory.CreateDirectory(Config.ThumbsDir);
            File.WriteAllText(Config.ThumbPath(clipId), "thumbnail");
            return true;
        };
        ThumbnailWorker.Ensure(_operations);
        Task<bool>[] requests = [];
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            requests = Enumerable.Range(0, 8).Select(_ => ThumbnailWorker.GetAsync(id, _operations)).ToArray();
        }
        finally { release.Set(); }
        await ThumbnailWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(await Task.WhenAll(requests), Assert.True);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failed_thumbnail_backs_off_across_worker_runs_and_interactive_requests()
    {
        var id = Clip("broken.mp4");
        int calls = 0;
        _operations.Thumbnail = (_, _) => { calls++; return false; };
        for (int i = 0; i < 3; i++)
        {
            ThumbnailWorker.Ensure(_operations);
            await ThumbnailWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await ThumbnailWorker.GetAsync(id, _operations));
        }
        Assert.Equal(1, calls);
        TestDb.Exec("UPDATE clips SET thumb_retry_at=0 WHERE id=$id", ("$id", id));
        Assert.False(await ThumbnailWorker.GetAsync(id, _operations));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cloud_clips_do_not_trigger_local_probes_or_retry_writes()
    {
        var id = Clip("cloud.mp4");
        TestDb.Exec("UPDATE clips SET storage='cloud' WHERE id=$id", ("$id", id));
        var operations = new FakeClipOperations();
        ThumbnailWorker.Ensure(operations);
        await ThumbnailWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await ThumbnailWorker.GetAsync(id, operations));
        Assert.Equal(0, new ClipRepository().GetById(id)!.HasThumb);
    }

    [Fact]
    public async Task Source_changed_during_encoding_does_not_publish_a_stale_thumbnail()
    {
        var id = Clip("changing.mp4");
        _operations.Thumbnail = (clipId, path) =>
        {
            Directory.CreateDirectory(Config.ThumbsDir);
            File.WriteAllText(Config.ThumbPath(clipId), "stale");
            File.AppendAllText(path, "changed");
            return true;
        };
        Assert.False(await ThumbnailWorker.GetAsync(id, _operations));
        Assert.False(File.Exists(Config.ThumbPath(id)));
        Assert.Equal(0, new ClipRepository().GetById(id)!.HasThumb);
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