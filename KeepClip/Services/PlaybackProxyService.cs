using System.Collections.Concurrent;

namespace KeepClip.Services;

public sealed class PlaybackProxyService : IAsyncDisposable
{
    public sealed record Status(
        bool ready,
        bool preparing,
        double progress,
        string stage,
        string? error,
        string? url,
        bool proxy,
        double source_fps);

    private sealed class Job
    {
        public required string Fingerprint { get; init; }
        public double Progress { get; set; }
        public string Stage { get; set; } = "preparing";
        public string? Error { get; set; }
        public bool Finished { get; set; }
        public bool Canceled { get; set; }
        public bool Settled { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Completion { get; set; } = Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<long, Job> _jobs = new();
    private readonly ConcurrentDictionary<long, (string Fingerprint, Lazy<Media.PlaybackVideoInfo?> Info)> _probes = new();
    private readonly object _jobGate = new();
    private readonly ClipOperations _operations;
    private readonly SemaphoreSlim _slots = new(1, 1);
    private readonly HashSet<Task> _running = new();
    private volatile bool _disposed;

    public PlaybackProxyService() : this(new ClipOperations()) { }
    internal PlaybackProxyService(ClipOperations operations) => _operations = operations;

    public Status GetOrStart(Clip clip, bool start = true)
    {
        lock (_jobGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetOrStartCore(clip, start);
        }
    }

    private Status GetOrStartCore(Clip clip, bool start)
    {
        if (clip.Storage == "cloud")
            return ReadyOriginal(clip, 0);
        if (string.IsNullOrWhiteSpace(clip.Filepath) || !File.Exists(clip.Filepath))
            return new(false, false, 0, "error", "Plik nie istnieje na dysku.", null, false, 0);

        var fingerprint = Fingerprint(clip.Filepath);
        var probe = _probes.GetOrAdd(clip.Id, _ => (fingerprint, new(() => _operations.ProbePlaybackVideo(clip.Filepath))));
        if (probe.Fingerprint != fingerprint)
        {
            probe = (fingerprint, new(() => _operations.ProbePlaybackVideo(clip.Filepath)));
            _probes[clip.Id] = probe;
            CancelJob(clip.Id);
        }

        var info = probe.Info.Value;
        var fps = info?.Fps ?? 0;
        if (fps <= 120)
            return ReadyOriginal(clip, fps);

        var output = CachePath(clip.Id, fingerprint);
        if (File.Exists(output) && new FileInfo(output).Length > 1024)
        {
            Touch(output);
            return new(true, false, 1, "ready", null, ProxyUrl(clip.Id, fingerprint), true, fps);
        }
        if (!start)
            return new(false, false, 0, "notReady", null, null, true, fps);

        Job job;
        lock (_jobGate)
        {
            if (_jobs.TryGetValue(clip.Id, out var completed) && completed.Finished) CancelJob(clip.Id);
            if (!_jobs.TryGetValue(clip.Id, out job!))
            {
                RemoveOldFiles(clip.Id, output);
                job = new Job { Fingerprint = fingerprint, Progress = 0, Stage = "preparing" };
                _jobs[clip.Id] = job;
                job.Completion = Task.Run(() => GenerateAsync(clip, job, output, info));
                _running.RemoveWhere(task => task.IsCompleted);
                _running.Add(job.Completion);
            }
        }

        if (job.Fingerprint != fingerprint)
        {
            CancelJob(clip.Id);
            return GetOrStart(clip);
        }

        lock (job)
        {
            if (job.Finished && job.Error is null && File.Exists(output))
                return new(true, false, 1, "ready", null, ProxyUrl(clip.Id, fingerprint), true, fps);
            if (job.Error is not null)
                return new(false, false, job.Progress, "error", job.Error, OriginalUrl(clip), false, fps);
            return new(false, true, job.Progress, job.Stage, null, null, true, fps);
        }
    }

    public bool TryGetReadyPath(Clip clip, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(clip.Filepath) || !File.Exists(clip.Filepath)) return false;
        var candidate = CachePath(clip.Id, Fingerprint(clip.Filepath));
        if (!File.Exists(candidate) || new FileInfo(candidate).Length <= 1024) return false;
        path = candidate;
        Touch(path);
        return true;
    }

    public void Invalidate(long clipId)
    {
        lock (_jobGate)
        {
            CancelJob(clipId);
            _probes.TryRemove(clipId, out _);
            RemoveOldFiles(clipId, keep: null);
        }
    }

    public void Cancel(long clipId) { lock (_jobGate) CancelJob(clipId); }

    internal Task WaitForCurrentJobAsync(long clipId)
    {
        lock (_jobGate) return _jobs.TryGetValue(clipId, out var job) ? job.Completion : Task.CompletedTask;
    }

    private void CancelJob(long clipId)
    {
        if (!_jobs.TryRemove(clipId, out var job)) return;
        lock (job)
        {
            job.Canceled = true;
            if (!job.Settled) job.Cancellation.Cancel();
        }
    }

    private static Status ReadyOriginal(Clip clip, double fps) =>
        new(true, false, 1, "ready", null, OriginalUrl(clip), false, fps);

    private static string OriginalUrl(Clip clip) => $"/video/{clip.Id}?v={clip.SizeBytes}";
    private static string ProxyUrl(long clipId, string fingerprint) => $"/playback/{clipId}?v={fingerprint}";
    private static string CachePath(long clipId, string fingerprint) =>
        Path.Combine(Config.PlaybackDir, $"{clipId}_{fingerprint}.mp4");

    private static string Fingerprint(string path)
    {
        var info = new FileInfo(path);
        return $"{info.Length:x}_{info.LastWriteTimeUtc.Ticks:x}";
    }

    private async Task GenerateAsync(Clip clip, Job job, string output, Media.PlaybackVideoInfo? info)
    {
        var jobOutput = Path.Combine(Config.PlaybackDir, $".{clip.Id}_{Guid.NewGuid():N}.mp4");
        bool entered = false;
        try
        {
            await _slots.WaitAsync(job.Cancellation.Token);
            entered = true;
            if (_disposed) return;
            job.Cancellation.Token.ThrowIfCancellationRequested();
            var duration = clip.Duration > 0
                ? clip.Duration
                : _operations.ProbeDuration(clip.Filepath!) ?? 0;
            var result = _operations.CreatePlaybackProxy(clip.Filepath!, jobOutput, duration, value =>
            {
                lock (job)
                {
                    job.Stage = "converting";
                    job.Progress = value;
                }
            }, job.Cancellation.Token, info);

            lock (job)
            {
                if (job.Canceled) return;
                if (result.Ok)
                {
                    File.Move(jobOutput, output, overwrite: true);
                    Touch(output);
                    TrimCache(output);
                }
                job.Finished = result.Ok;
                job.Progress = result.Ok ? 1 : job.Progress;
                job.Stage = result.Ok ? "ready" : "error";
                job.Error = result.Ok ? null : result.Message;
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lock (job)
            {
                job.Stage = "error";
                job.Error = $"Nie udało się przygotować podglądu: {ex.Message}";
            }
        }
        finally
        {
            try { if (File.Exists(jobOutput)) File.Delete(jobOutput); } catch (IOException) { }
            if (entered) _slots.Release();
            lock (job)
            {
                job.Settled = true;
                job.Cancellation.Dispose();
            }
        }
    }

    private static void Touch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void TrimCache(string keep, long maximumBytes = 2L * 1024 * 1024 * 1024)
    {
        try
        {
            var files = new DirectoryInfo(Config.PlaybackDir).EnumerateFiles("*.mp4")
                .Where(file => !file.Name.StartsWith('.')).OrderBy(file => file.LastAccessTimeUtc).ToList();
            long total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= maximumBytes) break;
                if (string.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { long size = file.Length; file.Delete(); total -= size; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_jobGate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var id in _jobs.Keys) CancelJob(id);
            pending = _running.ToArray();
        }
        await Task.WhenAll(pending);
        _slots.Dispose();
    }

    private static void RemoveOldFiles(long clipId, string? keep)
    {
        try
        {
            Directory.CreateDirectory(Config.PlaybackDir);
            foreach (var file in Directory.EnumerateFiles(Config.PlaybackDir, $"{clipId}_*.mp4"))
            {
                if (keep is not null && string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        catch (IOException) { }
    }
}
