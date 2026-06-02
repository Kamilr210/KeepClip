namespace KeepClip;

/// <summary>
/// Static paths and runtime knobs. Mirrors the Python <c>config.py</c> so the C#
/// backend resolves the exact same data/, frontend/, and tools/ locations.
/// </summary>
public static class Config
{
    /// <summary>
    /// Install/repo root that contains <c>frontend/</c>, <c>data/</c>, <c>tools/</c>.
    /// In a published build this is the folder next to the exe; in dev it is the
    /// repo root found by walking up from the build output directory.
    /// </summary>
    public static readonly string AppRoot = FindAppRoot();

    public static readonly string DataDir     = Path.Combine(AppRoot, "data");
    public static readonly string ThumbsDir   = Path.Combine(DataDir, "thumbs");
    public static readonly string TmpDir      = Path.Combine(DataDir, "tmp");
    public static readonly string ToolsBin    = Path.Combine(AppRoot, "tools", "bin");
    public static readonly string Ffmpeg      = Path.Combine(ToolsBin, "ffmpeg.exe");
    public static readonly string Ffprobe     = Path.Combine(ToolsBin, "ffprobe.exe");
    public static readonly string FrontendDir = Path.Combine(AppRoot, "frontend");
    public static readonly string DbPath      = Path.Combine(DataDir, "klipy.db");

    /// <summary>
    /// Whisper GGML models live here, downloaded on first transcription (gitignored).
    /// faster-whisper cached CTranslate2 weights under HF's cache; Whisper.net uses a
    /// single self-contained <c>.bin</c> file we manage ourselves next to the app.
    /// </summary>
    public static readonly string ModelsDir = Path.Combine(AppRoot, "models");

    /// <summary>
    /// Compile-time fallback for the clips folder. The user overrides this at
    /// runtime via in-app settings (<see cref="Settings"/>). Defaults to the
    /// typical NVIDIA ShadowPlay location under the current user.
    /// </summary>
    public static readonly string DefaultClipsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "NVIDIA");

    /// <summary>
    /// Cut/compressed fragments are saved in this subfolder of the clips root, so
    /// they sit alongside the game folders and get picked up by the scanner.
    /// </summary>
    public const string CutsSubdir = "Wycinki";

    public static readonly HashSet<string> VideoExts =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".mov", ".avi", ".webm" };

    /// <summary>On-disk path of a clip's cached JPEG thumbnail (data/thumbs/{id}.jpg).</summary>
    public static string ThumbPath(long clipId) => Path.Combine(ThumbsDir, $"{clipId}.jpg");

    // ---- Google Drive cloud offload ----

    /// <summary>
    /// The OAuth "Desktop app" client in Google's downloaded JSON format (an
    /// <c>"installed"</c> object holding <c>client_id</c>/<c>client_secret</c>).
    /// Gitignored — never committed. Shipped builds embed the author's client so end
    /// users go straight to "Połącz"; until this file is present the cloud feature
    /// reports "not configured" and the UI shows the one-time setup panel.
    /// </summary>
    public static readonly string GoogleClientPath = Path.Combine(DataDir, "google_client.json");

    /// <summary>
    /// Drive folder (created on first upload, then remembered) that all offloaded clips
    /// live in. With the <c>drive.file</c> scope the app only ever sees files it created.
    /// </summary>
    public const string DriveFolderName = "KeepClip";

    /// <summary>
    /// Windows Credential Manager target under which the Drive refresh token is stored
    /// (see <see cref="CredentialStore"/>) — never in plaintext on disk.
    /// </summary>
    public const string DriveTokenTarget = "KeepClip:GoogleDriveRefreshToken";

    // ---- Whisper config ----
    public const string WhisperModel = "large-v3-turbo";
    public const string WhisperLang  = "pl";
    /// <summary>"auto" uses the NVIDIA GPU (CUDA) if present, otherwise the CPU. Force with "cuda"/"cpu".</summary>
    public const string WhisperDevice = "auto";
    public const string WhisperComputeType = "int8";
    /// <summary>Split a transcript segment whenever an internal pause is at least this many seconds.</summary>
    public const double WhisperSplitGapSeconds = 1.5;

    /// <summary>
    /// Which GGML model to download/load. Defaults to <see cref="WhisperModel"/>
    /// (large-v3-turbo, full Polish accuracy); override with the
    /// <c>KEEPCLIP_WHISPER_MODEL</c> env var (tiny|base|small|medium|large-v3|
    /// large-v3-turbo) on weak hardware or for a fast smoke test.
    /// </summary>
    public static readonly string WhisperGgmlType =
        (Environment.GetEnvironmentVariable("KEEPCLIP_WHISPER_MODEL") ?? WhisperModel).Trim().ToLowerInvariant();

    /// <summary>On-disk path of the downloaded GGML weights (Whisper.net's whisper.cpp format).</summary>
    public static readonly string WhisperModelPath = Path.Combine(ModelsDir, $"ggml-{WhisperGgmlType}.bin");

    private static string FindAppRoot()
    {
        // Walk up from the executable's base directory until we find the folder
        // that holds frontend/index.html. Works both in dev (bin/Debug/netX) and
        // in a published layout where frontend/ sits next to the exe.
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
