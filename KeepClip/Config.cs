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

    public const string WhisperLang  = "pl";
    public const string WhisperDevice = "auto";
    public const string WhisperComputeType = "int8";
    public const double WhisperSplitGapSeconds = 1.5;

    // Kolejność schodzenia, gdy sprzęt nie udźwignie mocniejszego modelu. Pierwszy pasujący
    // do pamięci GPU jest domyślny; kolejne są używane, gdy poprzedni nie wystartuje.
    public static readonly string[] WhisperModelLadder =
        { "large-v3", "large-v3-turbo", "small" };

    // Zmierzone szczytowe zużycie pamięci GPU to ~4,3 GB dla large-v3 i ~2,3 GB dla turbo.
    // Progi zostawiają zapas na pulpit i grę działającą w tle.
    public static readonly Dictionary<string, long> WhisperModelMinVram =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["large-v3"]       = 6L * 1024 * 1024 * 1024,
            ["large-v3-turbo"] = 3L * 1024 * 1024 * 1024,
            ["small"]          = 0,
        };

    // Jawny wybór użytkownika wyłącza automatyczny dobór modelu.
    public static readonly string? WhisperModelOverride =
        Environment.GetEnvironmentVariable("KEEPCLIP_WHISPER_MODEL")?.Trim().ToLowerInvariant()
            is { Length: > 0 } forced ? forced : null;

    public static string WhisperModelPath(string ggmlType) =>
        Path.Combine(ModelsDir, $"ggml-{ggmlType}.bin");

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
