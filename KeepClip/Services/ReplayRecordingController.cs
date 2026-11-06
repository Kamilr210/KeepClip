namespace KeepClip.Services;

internal enum ReplayStartResult { Started, Deferred, Failed }

internal abstract class ReplayCaptureBackend
{
    internal abstract bool Running { get; }
    internal abstract bool HasBuffer { get; }
    internal abstract ReplayStartResult Start(bool freshRing, Func<bool> stillAllowed);
    internal abstract void Stop();
    internal abstract void Clear();
    internal virtual void ResetFailure(bool newConfiguration = false) { }
}

// ReplayService serializes all controller calls with its capture lock.
internal sealed class ReplayRecordingController(ReplayCaptureBackend capture, Func<string?> detectGame, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private bool _active, _hasBuffer, _faulted, _captureExpected;
    private bool _clearPending = true;
    private string? _signature, _foregroundGame, _bufferGame;
    private long _failedAt;
    private TimeSpan? _retryDelay;

    internal bool Enabled { get; private set; }
    internal bool GamesOnly { get; private set; }
    internal bool Saving { get; private set; }
    internal bool Running => capture.Running;
    internal bool WaitingForGame => _active && Enabled && GamesOnly && _foregroundGame is null;
    internal bool CanSave => _active && Enabled && !Saving && !_clearPending && _hasBuffer && capture.HasBuffer;
    internal string? BufferGame => _bufferGame;
    internal string? Configuration => _signature;
    internal string? Error { get; private set; }

    internal void Configure(bool enabled, bool gamesOnly, string signature)
    {
        bool changed = !_active || Enabled != enabled || GamesOnly != gamesOnly || _signature != signature;
        _active = true;
        Enabled = enabled;
        GamesOnly = gamesOnly;
        _signature = signature;
        if (changed || _faulted)
        {
            StopCapture();
            _clearPending = true;
            _retryDelay = null;
            _faulted = false;
            capture.ResetFailure(newConfiguration: true);
        }
        Tick();
    }

    internal void Tick()
    {
        if (!_active || !Enabled)
        {
            if (_captureExpected || capture.Running) StopCapture();
            if (_clearPending && !Saving) ClearBuffer();
            return;
        }

        var game = GamesOnly ? ReadGame() : null;
        if (GamesOnly && !string.Equals(game, _foregroundGame, StringComparison.OrdinalIgnoreCase))
        {
            _foregroundGame = game;
            _retryDelay = null;
            _faulted = false;
            if (_captureExpected || capture.Running) StopCapture();
            capture.ResetFailure();
            if (game is not null && !string.Equals(game, _bufferGame, StringComparison.OrdinalIgnoreCase)) _clearPending = true;
        }
        if (GamesOnly && game is null)
        {
            if (_captureExpected || capture.Running) StopCapture();
            if (_clearPending && !Saving) ClearBuffer();
            return;
        }
        if (_clearPending)
        {
            if (_captureExpected || capture.Running) StopCapture();
            if (Saving) return;
            if (!ClearBuffer()) return;
        }
        if (capture.Running || _captureExpected || Saving || _faulted) return;
        if (_retryDelay is { } delay && _clock.GetElapsedTime(_failedAt) < delay) return;
        _retryDelay = null;

        var result = capture.Start(!_hasBuffer, () => _active && Enabled && (!GamesOnly || game is not null && string.Equals(ReadGame(), game, StringComparison.OrdinalIgnoreCase)));
        if (result == ReplayStartResult.Started)
        {
            _captureExpected = true;
            _hasBuffer = true;
            _bufferGame = game;
        }
        else if (result == ReplayStartResult.Failed) _faulted = true;
    }

    internal void CaptureExited(TimeSpan retryDelay, bool terminal)
    {
        StopCapture();
        if (!_active || !Enabled) return;
        _failedAt = _clock.GetTimestamp();
        _retryDelay = retryDelay;
        _faulted = terminal;
    }

    internal bool TryBeginSave(out string? game)
    {
        game = _bufferGame;
        if (!CanSave) return false;
        Saving = true;
        return true;
    }

    internal void EndSave()
    {
        Saving = false;
        Tick();
    }

    internal void Shutdown()
    {
        _active = false;
        _foregroundGame = null;
        _retryDelay = null;
        _clearPending = true;
        StopCapture();
        if (!Saving) ClearBuffer();
    }

    private string? ReadGame()
    {
        try { return detectGame(); }
        catch { return null; }
    }

    private bool ClearBuffer()
    {
        try { capture.Clear(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            return false;
        }
        _clearPending = false;
        _hasBuffer = false;
        _bufferGame = null;
        Error = null;
        return true;
    }

    private void StopCapture()
    {
        capture.Stop();
        _captureExpected = false;
    }
}