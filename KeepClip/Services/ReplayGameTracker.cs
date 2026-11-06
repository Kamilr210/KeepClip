namespace KeepClip.Services;

internal sealed class ReplayGameTracker(Func<string?> foregroundGame, TimeProvider? clock = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string? _game;
    private long _lastSeen;

    internal void Observe()
    {
        lock (_gate) ObserveCore();
    }

    internal string Resolve(int durationSeconds)
    {
        lock (_gate)
        {
            ObserveCore();
            return _game is not null && _clock.GetElapsedTime(_lastSeen) <= TimeSpan.FromSeconds(durationSeconds) ? _game : "Pulpit";
        }
    }

    internal void Reset()
    {
        lock (_gate) _game = null;
    }

    private void ObserveCore()
    {
        var game = foregroundGame();
        if (game is null) return;
        _game = game;
        _lastSeen = _clock.GetTimestamp();
    }
}