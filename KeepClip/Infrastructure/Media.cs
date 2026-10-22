namespace KeepClip.Infrastructure;

public static class Media
{
    public readonly record struct PlaybackVideoInfo(string Codec, double Fps, int Width, int Height);
    private static readonly MediaProcessor Processor = new(new MediaProcessRunner());

    static Media()
    {
        Directory.CreateDirectory(Config.ThumbsDir);
        Directory.CreateDirectory(Config.TmpDir);
    }

    public static double? ProbeDuration(string path) => Processor.ProbeDuration(path);
    public static PlaybackVideoInfo? ProbePlaybackVideo(string path) => Processor.ProbePlaybackVideo(path);
    public static (bool Ok, string Message) CreatePlaybackProxy(string source, string output, double duration, Action<double>? progress = null) => Processor.CreatePlaybackProxy(source, output, duration, progress);
    public static int ProbeDecodeErrors(string path, double seconds = 5.0) => Processor.ProbeDecodeErrors(path, seconds);
    public static bool TryRemux(string src, string dst, double skipSeconds = 0.0) => Processor.TryRemux(src, dst, skipSeconds);
    public static (bool Ok, string? Output, string Message, double Trimmed) FixBrokenClip(string src, string? tmpOut = null) => Processor.FixBrokenClip(src, tmpOut);
    public static (bool Ok, string Message, Dictionary<string, object?> Stats) CutClip(string src, string output, double start, double end, double? targetSizeMb = null, Action<double, string>? progress = null) => Processor.CutClip(src, output, start, end, targetSizeMb, progress);
    public static bool MakeThumbnail(long clipId, string video, double atSeconds = 5.0) => Processor.MakeThumbnail(clipId, video, atSeconds);

    internal static string? ValidateCut(double start, double end, double? targetSizeMb)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0)
            return "Początek i koniec muszą być skończonymi liczbami, a początek nie może być ujemny.";
        if (end - start <= 0.1) return "Koniec musi być co najmniej 0.1s po początku.";
        if (end - start > 60 * 30) return "Maksymalnie 30 minut.";
        if (targetSizeMb is double size && (!double.IsFinite(size) || size <= 0 || size >= long.MaxValue / 1_000_000.0))
            return "Docelowy rozmiar musi być dodatnią, skończoną liczbą MB w obsługiwanym zakresie.";
        return null;
    }

}
