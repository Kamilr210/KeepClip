namespace KeepClip.Tests;

internal sealed class ReplayCaptureStub : ReplayCaptureBackend
{
    internal bool IsRunning;
    internal int Attempts;
    internal readonly List<bool> Starts = [];
    internal readonly List<string> Frames = [];
    internal Action? BeforeStart;
    internal ReplayStartResult Result = ReplayStartResult.Started;
    internal Exception? CleanupError;
    internal override bool Running => IsRunning;
    internal override bool HasBuffer => Frames.Count != 0;

    internal override ReplayStartResult Start(bool freshRing, Func<bool> stillAllowed)
    {
        Attempts++;
        BeforeStart?.Invoke();
        if (!stillAllowed()) return ReplayStartResult.Deferred;
        if (Result != ReplayStartResult.Started) return Result;
        Starts.Add(freshRing);
        if (freshRing) Frames.Clear();
        IsRunning = true;
        return ReplayStartResult.Started;
    }

    internal override void Stop() => IsRunning = false;
    internal override void Clear()
    {
        if (CleanupError is not null) throw CleanupError;
        Frames.Clear();
    }
}