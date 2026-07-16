namespace KeepClip;

public sealed record TranscribeSnapshot(
    bool running,
    int total,
    int done,
    object? current,          // Obiekt z identyfikatorem, nazwą pliku i grą albo null.
    string? error,
    string? finished_at,
    List<string> log_tail);

// Stan jest współdzielony przez proces roboczy i punkty końcowe, dlatego wszystkie odczyty oraz
// zapisy przechodzą przez jedną blokadę.
public static class TranscribeState
{
    public static readonly object Lock = new();

    public static bool Running;
    public static bool Cancel;
    public static int Total;
    public static int Done;
    public static object? Current;        // Identyfikator, nazwa pliku i gra albo null.
    public static string? Error;
    public static string? FinishedAt;

    private static readonly List<string> Log = new();

    public static void PushLog(string line)
    {
        lock (Lock)
        {
            Log.Add(line);
            if (Log.Count > 100) Log.RemoveRange(0, Log.Count - 100);
        }
    }

    public static TranscribeSnapshot Snapshot()
    {
        lock (Lock)
        {
            var tail = Log.Count > 8 ? Log.GetRange(Log.Count - 8, 8) : new List<string>(Log);
            return new TranscribeSnapshot(Running, Total, Done, Current, Error, FinishedAt, tail);
        }
    }
}
