namespace KeepClip;

public static class Config
{
    // W instalacji katalog leży obok EXE, a w trybie deweloperskim jest
    // odnajdywany przez przejście w górę drzewa katalogów.
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

    // Klient OAuth jest dostarczany w instalatorze, ale nie trafia do repozytorium.
    public static readonly string GoogleClientPath = Path.Combine(DataDir, "google_client.json");

    public const string DriveFolderName = "KeepClip";

    // Token odświeżania jest przechowywany w Menedżerze poświadczeń Windows.
    public const string DriveTokenTarget = "KeepClip:GoogleDriveRefreshToken";

    public const string WhisperModel = "large-v3-turbo";
    public const string WhisperLang  = "pl";
    public const string WhisperDevice = "auto";
    public const string WhisperComputeType = "int8";
    public const double WhisperSplitGapSeconds = 1.5;

    // Zmienna środowiskowa pozwala wybrać lżejszy model na słabszym sprzęcie.
    public static readonly string WhisperGgmlType =
        (Environment.GetEnvironmentVariable("KEEPCLIP_WHISPER_MODEL") ?? WhisperModel).Trim().ToLowerInvariant();

    public static readonly string WhisperModelPath = Path.Combine(ModelsDir, $"ggml-{WhisperGgmlType}.bin");

    private static string FindAppRoot()
    {
        // Obsługuje zarówno układ instalacji, jak i bin/Debug/netX w repozytorium.
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
