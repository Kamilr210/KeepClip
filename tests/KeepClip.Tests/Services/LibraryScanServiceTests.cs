namespace KeepClip.Tests.Services;

public class LibraryScanServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly Clock _clock = new();
    private int _scans;

    public LibraryScanServiceTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        Settings.SetClipsRoot(TestEnvironment.NewTempDir("scan-monitor"));
    }

    private Dictionary<string, object?> Scan(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _scans++;
        return new() { ["found"] = 1, ["added"] = 0, ["removed"] = 0, ["updated"] = 0 };
    }

    [Fact]
    public void Idle_polls_skip_work_but_periodic_and_manual_scans_still_run()
    {
        using var service = new LibraryScanService(Scan, _clock, watch: false);
        service.Scan(true, default);
        for (int i = 0; i < 20; i++)
        {
            _clock.Now += TimeSpan.FromSeconds(2.5);
            Assert.Equal(true, service.Scan(true, default)["skipped"]);
        }
        Assert.Equal(1, _scans);
        service.Scan(false, default);
        Assert.Equal(2, _scans);
        _clock.Now += TimeSpan.FromMinutes(1);
        service.Scan(true, default);
        Assert.Equal(3, _scans);
    }

    [Fact]
    public void Burst_is_coalesced_and_an_event_during_a_scan_is_not_lost()
    {
        LibraryScanService? service = null;
        using var monitor = new LibraryScanService(ct =>
        {
            var result = Scan(ct);
            if (_scans == 2) service!.Changed();
            return result;
        }, _clock, watch: false);
        service = monitor;
        monitor.Scan(true, default);
        for (int i = 0; i < 20; i++) monitor.Changed();
        Assert.Equal(true, monitor.Scan(true, default)["skipped"]);
        _clock.Now += TimeSpan.FromSeconds(3);
        monitor.Scan(true, default);
        _clock.Now += TimeSpan.FromSeconds(3);
        monitor.Scan(true, default);
        Assert.Equal(3, _scans);
        _clock.Now += TimeSpan.FromSeconds(3);
        Assert.Equal(true, monitor.Scan(true, default)["skipped"]);
    }

    [Fact]
    public void Pending_files_and_failed_scans_are_retried_without_another_file_event()
    {
        using var service = new LibraryScanService(ct =>
        {
            var result = Scan(ct);
            if (_scans == 1) result["error"] = "temporary failure";
            if (_scans == 2) result["pending"] = 1;
            return result;
        }, _clock, watch: false);
        for (int i = 0; i < 3; i++)
        {
            service.Scan(true, default);
            _clock.Now += TimeSpan.FromSeconds(3);
        }
        Assert.Equal(3, _scans);
        Assert.Equal(true, service.Scan(true, default)["skipped"]);
    }

    [Fact]
    public void Changing_the_library_root_forces_a_scan_and_cancellation_is_honored()
    {
        using var service = new LibraryScanService(Scan, _clock, watch: false);
        service.Scan(true, default);
        Settings.SetClipsRoot(TestEnvironment.NewTempDir("second-library"));
        service.Scan(true, default);
        Assert.Equal(2, _scans);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.Scan(true, cancelled.Token));
    }

    [Fact]
    public async Task Watcher_detects_a_file_in_a_nested_test_directory()
    {
        using var service = new LibraryScanService(Scan, _clock);
        service.Scan(true, default);
        var directory = Directory.CreateDirectory(Path.Combine(Settings.GetClipsRoot(), "game"));
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "new.mp4"), "synthetic");
        _clock.Now += TimeSpan.FromSeconds(3);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_scans == 1)
        {
            service.Scan(true, timeout.Token);
            if (_scans == 1) await Task.Delay(20, timeout.Token);
        }
        Assert.Equal(2, _scans);
    }
}