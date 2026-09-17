namespace KeepClip;

public static class Config
{
    public static readonly string AppRoot = FindAppRoot();

    public static readonly string DataDir     = Path.Combine(AppRoot, "data");
    public static readonly string ThumbsDir   = Path.Combine(DataDir, "thumbs");
    public static readonly string TmpDir      = Path.Combine(DataDir, "tmp");
    public static readonly string PlaybackDir = Path.Combine(DataDir, "playback");
    public static readonly string ToolsBin    = Path.Combine(AppRoot, "tools", "bin");
    public static readonly string Ffmpeg      = Path.Combine(ToolsBin, "ffmpeg.exe");
    public static readonly string Ffprobe     = Path.Combine(ToolsBin, "ffprobe.exe");
    public static readonly string FrontendDir = Path.Combine(AppRoot, "frontend");
    public static readonly string DbPath      = Path.Combine(DataDir, "klipy.db");

    public static readonly string ModelsDir = Path.Combine(AppRoot, "models");

    public static readonly string DefaultClipsRoot =
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    public const string CutsSubdir = "Wycinki";

    public static readonly HashSet<string> VideoExts =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".mov", ".avi", ".webm" };

    public static string ThumbPath(long clipId) => Path.Combine(ThumbsDir, $"{clipId}.jpg");

    public static readonly string GoogleClientPath = Path.Combine(DataDir, "google_client.json");

    public const string DriveFolderName = "KeepClip";

    public const string DriveTokenTarget = "KeepClip:GoogleDriveRefreshToken";

    public static readonly string[] UiLanguages = { "pl", "en", "ru", "uk" };

    public const string DefaultLanguage = "en";

    public const string WhisperDevice = "auto";
    public const string WhisperComputeType = "int8";
    public const double WhisperSplitGapSeconds = 1.5;

    public static readonly string[] WhisperModelLadder =
        { "large-v3", "large-v3-turbo", "small" };

    public static readonly Dictionary<string, long> WhisperModelMinVram =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["large-v3"]       = 6L * 1024 * 1024 * 1024,
            ["large-v3-turbo"] = 3L * 1024 * 1024 * 1024,
            ["small"]          = 0,
        };

    public static readonly string? WhisperModelOverride =
        Environment.GetEnvironmentVariable("KEEPCLIP_WHISPER_MODEL")?.Trim().ToLowerInvariant()
            is { Length: > 0 } forced ? forced : null;

    public static string WhisperModelPath(string ggmlType) =>
        Path.Combine(ModelsDir, $"ggml-{ggmlType}.bin");

    private static string FindAppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "frontend", "index.html")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
