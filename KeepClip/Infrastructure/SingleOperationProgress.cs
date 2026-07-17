namespace KeepClip.Infrastructure;

public static class SingleOperationProgress
{
    private static readonly object Gate = new();
    private static string _id = "";
    private static string _kind = "";
    private static string _stage = "";
    private static long _clipId;
    private static double _progress;
    private static bool _active;
    private static bool? _success;
    private static string? _error;

    public static string Start(string kind, long clipId)
    {
        lock (Gate)
        {
            _id = Guid.NewGuid().ToString("N");
            _kind = kind;
            _stage = "preparing";
            _clipId = clipId;
            _progress = 0;
            _active = true;
            _success = null;
            _error = null;
            return _id;
        }
    }

    public static void Report(string id, double progress, string stage)
    {
        lock (Gate)
        {
            if (!_active || _id != id) return;
            _progress = Math.Max(_progress, Math.Clamp(progress, 0, 0.99));
            _stage = stage;
        }
    }

    public static void Finish(string id, bool success, string? error = null)
    {
        lock (Gate)
        {
            if (_id != id) return;
            _active = false;
            _success = success;
            _error = error;
            if (success) _progress = 1;
        }
    }

    public static Dictionary<string, object?> Snapshot()
    {
        lock (Gate)
        {
            return new()
            {
                ["id"] = _id,
                ["kind"] = _kind,
                ["stage"] = _stage,
                ["clip_id"] = _clipId,
                ["progress"] = _progress,
                ["active"] = _active,
                ["success"] = _success,
                ["error"] = _error,
            };
        }
    }
}
