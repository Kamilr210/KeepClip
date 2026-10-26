namespace KeepClip.Tests.Workers;

public class TranscribeWorkerTests
{
    private readonly FakeClipOperations _operations = new() { Duration = _ => 10, Thumbnail = (_, _) => false };
    private readonly string _root = TestEnvironment.NewTempDir("transcribe-worker");

    public TranscribeWorkerTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        lock (TranscribeState.Lock)
        {
            TranscribeState.Running = false;
            TranscribeState.Cancel = false;
            TranscribeState.Error = null;
            TranscribeState.Current = null;
        }
    }

    private long Clip(string name, double mtime = 1)
    {
        File.WriteAllText(Path.Combine(_root, name), "synthetic clip");
        return TestDb.AddClip(name: name, dir: _root, mtime: mtime);
    }

    private static async Task Complete() => await TranscribeWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Cancel_interrupts_current_decoding_and_preserves_the_previous_transcript()
    {
        var id = Clip("cancelled.mp4");
        new SegmentRepository().Insert(id, 0, 1, "keep this transcript");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operations.CancellableTranscription = (_, _, cancellationToken) =>
        {
            entered.TrySetResult();
            Task.Delay(Timeout.Infinite, cancellationToken).GetAwaiter().GetResult();
            throw new InvalidOperationException("Cancelled decoding must not return segments");
        };
        Assert.True(TranscribeWorker.Start(true, out _, _operations));
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { Assert.True(TranscribeWorker.Cancel()); }
        await Complete();
        Assert.Equal("keep this transcript", Assert.Single(new SegmentRepository().ListByClipId(id))["text"]);
        Assert.Null(new ClipRepository().GetById(id)!.TranscribedAt);
        Assert.False(TranscribeState.Snapshot().running);
        Assert.Null(TranscribeState.Snapshot().error);
        Assert.Null(TranscribeState.Snapshot().current);
        Assert.False(TranscribeWorker.Cancel());
    }

    [Fact]
    public async Task Empty_library_completes_and_releases_the_running_state()
    {
        Assert.True(TranscribeWorker.Start(false, out var total, _operations));
        Assert.Equal(0, total);
        await Complete();
        var state = TranscribeState.Snapshot();
        Assert.False(state.running);
        Assert.Null(state.error);
        Assert.NotNull(state.finished_at);
        Assert.Contains(state.log_tail, line => line.Contains("Brak nowych klipów"));
    }

    [Fact]
    public async Task Normal_run_selects_only_untranscribed_clips_and_skips_cloud_and_missing_files()
    {
        var local = Clip("local.mp4");
        var existing = Clip("existing.mp4");
        var missing = TestDb.AddClip(name: "missing.mp4", dir: _root);
        var cloud = TestDb.AddClip(name: "cloud.mp4", dir: _root);
        TestDb.Exec("UPDATE clips SET transcribed_at='old' WHERE id=$id", ("$id", existing));
        TestDb.Exec("UPDATE clips SET storage='cloud' WHERE id=$id", ("$id", cloud));
        _operations.Transcription = (path, _) => { Assert.EndsWith("local.mp4", path); return new() { new(1, 2, "test") }; };
        _operations.Thumbnail = (_, _) => true;

        Assert.True(TranscribeWorker.Start(false, out var total, _operations));
        Assert.Equal(3, total);
        await Complete();

        var repository = new ClipRepository();
        Assert.Null(repository.GetById(missing));
        Assert.NotNull(repository.GetById(cloud));
        Assert.Equal("old", repository.GetById(existing)!.TranscribedAt);
        Assert.NotNull(repository.GetById(local)!.TranscribedAt);
        Assert.Equal(10, repository.GetById(local)!.Duration);
        Assert.Equal(1, repository.GetById(local)!.HasThumb);
        Assert.Equal("test", Assert.Single(new SegmentRepository().ListByClipId(local))["text"]);
        Assert.Equal(3, TranscribeState.Snapshot().done);
    }

    [Fact]
    public async Task Force_retranscribes_existing_clips_and_continues_after_a_decode_error()
    {
        var bad = Clip("bad.mp4", 2);
        var good = Clip("good.mp4", 1);
        TestDb.Exec("UPDATE clips SET transcribed_at='old'");
        new SegmentRepository().Insert(bad, 0, 1, "preserve");
        _operations.Transcription = (path, _) => path.EndsWith("bad.mp4") ? throw new IOException("decode failed") : new() { new(1, 2, "new") };

        Assert.True(TranscribeWorker.Start(true, out var total, _operations));
        Assert.Equal(2, total);
        await Complete();

        Assert.Equal("preserve", Assert.Single(new SegmentRepository().ListByClipId(bad))["text"]);
        Assert.Equal("new", Assert.Single(new SegmentRepository().ListByClipId(good))["text"]);
        Assert.Equal("decode failed", TranscribeState.Snapshot().error);
        Assert.Equal(2, TranscribeState.Snapshot().done);
        Assert.Null(TranscribeState.Snapshot().current);
    }

    [Fact]
    public async Task Cancellation_finishes_the_current_clip_and_prevents_the_next_one_and_duplicate_start()
    {
        var first = Clip("first.mp4", 2);
        var second = Clip("second.mp4", 1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _operations.Transcription = (_, _) => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new() { new(0, 1, "done") }; };
        Assert.True(TranscribeWorker.Start(false, out _, _operations));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(TranscribeWorker.Start(false, out _, _operations));
            lock (TranscribeState.Lock) TranscribeState.Cancel = true;
        }
        finally { release.Set(); await Complete(); }

        Assert.NotNull(new ClipRepository().GetById(first)!.TranscribedAt);
        Assert.Null(new ClipRepository().GetById(second)!.TranscribedAt);
        Assert.Equal(1, TranscribeState.Snapshot().done);
        Assert.False(TranscribeState.Cancel);
        Assert.False(TranscribeState.Snapshot().running);
    }

    [Fact]
    public async Task Database_failure_during_replacement_preserves_the_previous_transcript()
    {
        var id = Clip("transaction.mp4");
        new SegmentRepository().Insert(id, 0, 1, "original");
        TestDb.Exec("CREATE TRIGGER reject_worker_segment BEFORE INSERT ON segments WHEN NEW.text='fail' BEGIN SELECT RAISE(ABORT, 'test insert failure'); END");
        _operations.Transcription = (_, _) => new() { new(0, 1, "partial"), new(1, 2, "fail") };
        try
        {
            Assert.True(TranscribeWorker.Start(true, out _, _operations));
            await Complete();
            Assert.Equal("original", Assert.Single(new SegmentRepository().ListByClipId(id))["text"]);
            Assert.Null(new ClipRepository().GetById(id)!.TranscribedAt);
            Assert.Empty(new SegmentRepository().Search("\"partial\"*", null, 10));
            Assert.NotNull(TranscribeState.Snapshot().error);
        }
        finally { TestDb.Exec("DROP TRIGGER reject_worker_segment"); }
    }
}