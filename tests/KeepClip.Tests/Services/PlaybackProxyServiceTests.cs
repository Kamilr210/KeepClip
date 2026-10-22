namespace KeepClip.Tests.Services;

public class PlaybackProxyServiceTests
{
    private readonly FakeClipOperations _operations = new() { Video = _ => new("h264", 240, 1920, 1080), Duration = _ => 12 };
    private readonly PlaybackProxyService _playback;
    private readonly Clip _clip;

    public PlaybackProxyServiceTests()
    {
        var root = TestEnvironment.NewTempDir("playback");
        var path = Path.Combine(root, "source.mp4");
        File.WriteAllBytes(path, new byte[32]);
        _clip = new Clip(Random.Shared.Next(1_000_000, int.MaxValue), "Game", "source.mp4", 0, 32, 0, 0, null, 0, "local", null, path);
        _playback = new PlaybackProxyService(_operations);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void Ordinary_frame_rates_use_the_original_without_encoding(int fps)
    {
        int probes = 0;
        _operations.Video = _ => { probes++; return new("h264", fps, 1920, 1080); };
        var first = _playback.GetOrStart(_clip);
        var second = _playback.GetOrStart(_clip);
        Assert.True(first.ready);
        Assert.False(first.proxy);
        Assert.Equal(fps, first.source_fps);
        Assert.StartsWith($"/video/{_clip.Id}?", first.url);
        Assert.Equal(first, second);
        Assert.Equal(1, probes);
    }

    [Fact]
    public void Inspecting_a_high_frame_rate_clip_does_not_start_an_encoder()
    {
        var status = _playback.GetOrStart(_clip, start: false);
        Assert.False(status.ready);
        Assert.False(status.preparing);
        Assert.True(status.proxy);
        Assert.Equal("notReady", status.stage);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_job_and_reuse_the_completed_proxy()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        _operations.Proxy = (_, output, duration, progress) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(12, duration);
            progress!(0.4);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            File.WriteAllBytes(output, new byte[2048]);
            return (true, "ok");
        };
        try
        {
            Assert.True(_playback.GetOrStart(_clip).preparing);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var states = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => _playback.GetOrStart(_clip))));
            Assert.All(states, state => { Assert.True(state.preparing); Assert.Equal(0.4, state.progress); });
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        var ready = await WaitFor(state => state.ready);
        Assert.True(ready.proxy);
        Assert.StartsWith($"/playback/{_clip.Id}?", ready.url);
        Assert.True(_playback.TryGetReadyPath(_clip, out var path));
        Assert.Equal(2048, new FileInfo(path).Length);
        Assert.Equal(1, calls);
        _playback.Invalidate(_clip.Id);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Encoder_failure_exposes_an_error_and_the_original_video(bool throws)
    {
        _operations.Proxy = (_, _, _, _) => throws ? throw new IOException("encoder failed") : (false, "encoder failed");
        _playback.GetOrStart(_clip);
        var failed = await WaitFor(state => state.error is not null);
        Assert.False(failed.ready);
        Assert.False(failed.preparing);
        Assert.Contains("encoder failed", failed.error);
        Assert.StartsWith($"/video/{_clip.Id}?", failed.url);
        Assert.False(_playback.TryGetReadyPath(_clip, out _));
        _playback.Invalidate(_clip.Id);
    }

    [Fact]
    public void Changed_source_is_probed_again()
    {
        int probes = 0;
        _operations.Video = _ => { probes++; return new("h264", 60, 1920, 1080); };
        _playback.GetOrStart(_clip);
        File.AppendAllText(_clip.Filepath!, "changed");
        _playback.GetOrStart(_clip);
        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task Canceled_old_job_cannot_delete_the_replacement_proxy()
    {
        using var oldEntered = new ManualResetEventSlim();
        using var releaseOld = new ManualResetEventSlim();
        Task oldCompletion = Task.CompletedTask;
        int calls = 0;
        _operations.Proxy = (_, output, _, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                oldEntered.Set();
                Assert.True(releaseOld.Wait(TimeSpan.FromSeconds(10)));
            }
            File.WriteAllBytes(output, Enumerable.Repeat((byte)call, 2048).ToArray());
            return (true, "ok");
        };
        try
        {
            _playback.GetOrStart(_clip);
            oldCompletion = _playback.WaitForCurrentJobAsync(_clip.Id);
            Assert.True(oldEntered.Wait(TimeSpan.FromSeconds(10)));
            _playback.Invalidate(_clip.Id);
            _playback.GetOrStart(_clip);
            await WaitFor(state => state.ready);
        }
        finally { releaseOld.Set(); }
        await oldCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(_playback.TryGetReadyPath(_clip, out var path));
        Assert.All(File.ReadAllBytes(path), value => Assert.Equal(2, value));
        Assert.Equal(2, calls);
        _playback.Invalidate(_clip.Id);
    }

    private async Task<PlaybackProxyService.Status> WaitFor(Func<PlaybackProxyService.Status, bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var state = _playback.GetOrStart(_clip);
            if (condition(state)) return state;
            await Task.Delay(10, cts.Token);
        }
    }
}