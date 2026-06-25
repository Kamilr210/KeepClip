namespace KeepClip.Infrastructure;

/// <summary>
/// Tracks the last time the frontend pinged us. The idle watcher (Phase 4, desktop
/// shell) exits the process once we've gone <see cref="IdleTimeoutSeconds"/> with no
/// activity. Mirrors <c>_touch_heartbeat</c> + the startup grace period in app.py.
/// </summary>
public static class Heartbeat
{
    public const int IdleTimeoutSeconds = 5 * 60;
    private const int GraceAtStartupSeconds = 60;

    private static long _lastMs =
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + GraceAtStartupSeconds * 1000L;

    public static void Touch()
        => Interlocked.Exchange(ref _lastMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static double IdleSeconds
        => (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Interlocked.Read(ref _lastMs)) / 1000.0;
}
