namespace KeepClip.Services;

public sealed class LibraryScanService : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Dictionary<string, object?>> _scan;
    private readonly TimeProvider _clock;
    private readonly bool _watch;
    private FileSystemWatcher? _watcher;
    private string? _root;
    private long _version;
    private long _scannedVersion = -1;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFullScan = DateTimeOffset.MinValue;
    private bool _pending;
    private bool _disposed;

    public LibraryScanService() : this(Scanner.Scan, TimeProvider.System) { }
    internal LibraryScanService(Func<CancellationToken, Dictionary<string, object?>> scan, TimeProvider clock, bool watch = true)
    {
        _scan = scan;
        _clock = clock;
        _watch = watch;
    }

    internal void Changed() => Interlocked.Increment(ref _version);

    public Dictionary<string, object?> Scan(bool automatic, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWatcher(Settings.GetClipsRoot());
            var now = _clock.GetUtcNow();
            var version = Interlocked.Read(ref _version);
            // Reconciliation also recovers missed watcher events and unavailable network watchers.
            bool due = now - _lastFullScan >= TimeSpan.FromMinutes(1);
            bool changed = version != _scannedVersion || _pending;
            if (automatic && !due && (!changed || now - _lastScan < TimeSpan.FromSeconds(2)))
                return new() { ["found"] = 0, ["added"] = 0, ["removed"] = 0, ["updated"] = 0, ["skipped"] = true };

            var result = _scan(cancellationToken);
            _lastScan = now;
            if (!result.ContainsKey("error"))
            {
                _lastFullScan = now;
                _scannedVersion = version;
                _pending = Convert.ToInt32(result.GetValueOrDefault("pending") ?? 0) > 0;
            }
            return result;
        }
    }

    private void EnsureWatcher(string root)
    {
        if (!string.Equals(root, _root, StringComparison.OrdinalIgnoreCase))
        {
            _watcher?.Dispose();
            _watcher = null;
            _root = root;
            _lastFullScan = DateTimeOffset.MinValue;
            Changed();
        }
        if (!_watch || _watcher is not null || !Directory.Exists(root)) return;
        FileSystemWatcher? watcher = null;
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => Changed();
    private void OnError(object sender, ErrorEventArgs args) => Changed();

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}