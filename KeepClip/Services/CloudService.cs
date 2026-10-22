using System.Globalization;
using System.Text.RegularExpressions;

namespace KeepClip.Services;

public static class CloudService
{
    private const string FolderIdSettingKey = "cloud_folder_id";
    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private static readonly CloudConnection Connection = new();

    public sealed record SyncResult(int Imported, int Updated, int Found, int Skipped);

    public static Task<bool> UploadClipAsync(long clipId) => UploadClipAsync(clipId, Connection);

    internal static async Task<bool> UploadClipAsync(long clipId, CloudConnection connection)
    {
        using var con = Db.Open();
        var row = con.QueryOne(
            "SELECT filepath, filename, game, duration, mtime, storage FROM clips WHERE id=$id", ("$id", clipId))
            ?? throw new FileNotFoundException(Strings.Get("clip.notFound"));
        if ((row["storage"] as string) == "cloud") return true;

        var filepath = row["filepath"] as string ?? "";
        var filename = row["filename"] as string ?? Path.GetFileName(filepath);
        if (string.IsNullOrEmpty(filepath) || !File.Exists(filepath))
            throw new FileNotFoundException(Strings.Get("clip.fileMissing"));

        var token = await connection.GetAccessTokenAsync();
        var folderId = await EnsureFolderAsync(token, connection);
        var appProperties = new Dictionary<string, string>
        {
            ["keepclip"] = "1",
            ["game"] = row["game"] as string ?? "_root",
            ["duration"] = Convert.ToDouble(row["duration"] ?? 0.0)
                .ToString("R", CultureInfo.InvariantCulture),
            ["mtime"] = Convert.ToDouble(row["mtime"] ?? 0.0)
                .ToString("R", CultureInfo.InvariantCulture),
        };
        string remoteId = await connection.UploadAsync(
            token, filename, folderId, filepath, VideoMime(filepath), appProperties);

        con.Exec("UPDATE clips SET storage='cloud', remote_id=$rid, remote_uploaded_at=$ts WHERE id=$id",
            ("$rid", remoteId), ("$ts", NowIso()), ("$id", clipId));

        connection.TrashLocalFile(filepath);
        connection.InvalidateAbout();
        return false;
    }

    public static Task<SyncResult> SyncFromCloudAsync() => SyncFromCloudAsync(Connection);

    internal static async Task<SyncResult> SyncFromCloudAsync(CloudConnection connection)
    {
        await SyncGate.WaitAsync();
        try
        {
            var token = await connection.GetAccessTokenAsync();
            var folderId = await EnsureFolderAsync(token, connection);
            var remoteFiles = await connection.ListFilesAsync(token, folderId);
            return ImportClips(remoteFiles);
        }
        finally
        {
            SyncGate.Release();
        }
    }

    internal static SyncResult ImportClips(IReadOnlyList<GoogleDrive.DriveFile> remoteFiles)
    {
        var videoFiles = remoteFiles
            .Where(file => Config.VideoExts.Contains(Path.GetExtension(file.Name)))
            .ToList();

        int imported = 0, updated = 0, skipped = remoteFiles.Count - videoFiles.Count;
        using var con = Db.Open();
        using var tx = con.BeginTransaction();

        foreach (var remote in videoFiles)
        {
            var existing = con.QueryOne(
                "SELECT id, filename, size_bytes, mtime, storage FROM clips WHERE remote_id=$rid LIMIT 1",
                ("$rid", remote.Id));

            double mtime = RemoteMtime(remote);
            double duration = RemoteDuration(remote);
            if (existing is not null)
            {
                bool changed = (existing["storage"] as string) != "cloud"
                               || (existing["filename"] as string) != remote.Name
                               || Convert.ToInt64(existing["size_bytes"] ?? 0L) != remote.Size
                               || Math.Abs(Convert.ToDouble(existing["mtime"] ?? 0.0) - mtime) > 0.001;
                con.Exec(
                    "UPDATE clips SET filename=$name, size_bytes=$size, mtime=$mtime, " +
                    "duration=CASE WHEN duration IS NULL OR duration <= 0 THEN $duration ELSE duration END, " +
                    "storage='cloud', remote_uploaded_at=COALESCE(remote_uploaded_at,$uploaded) WHERE id=$id",
                    ("$name", remote.Name), ("$size", remote.Size), ("$mtime", mtime),
                    ("$duration", duration), ("$uploaded", RemoteUploadedAt(remote)),
                    ("$id", Convert.ToInt64(existing["id"]!)));
                if (changed) updated++;
                continue;
            }

            existing = con.QueryOne(
                "SELECT id FROM clips WHERE filename=$name AND storage='cloud' AND remote_id IS NULL " +
                "ORDER BY id DESC LIMIT 1",
                ("$name", remote.Name));
            if (existing is not null)
            {
                con.Exec(
                    "UPDATE clips SET remote_id=$rid, size_bytes=$size, mtime=$mtime, " +
                    "duration=CASE WHEN duration IS NULL OR duration <= 0 THEN $duration ELSE duration END, " +
                    "remote_uploaded_at=COALESCE(remote_uploaded_at,$uploaded) WHERE id=$id",
                    ("$rid", remote.Id), ("$size", remote.Size), ("$mtime", mtime),
                    ("$duration", duration), ("$uploaded", RemoteUploadedAt(remote)),
                    ("$id", Convert.ToInt64(existing["id"]!)));
                updated++;
                continue;
            }

            string game = RemoteGame(remote);
            string filepath = UniqueCloudPath(con, game, remote.Name, remote.Id);
            con.Exec(
                "INSERT INTO clips(game, filename, filepath, size_bytes, mtime, duration, has_thumb, " +
                "storage, remote_id, remote_uploaded_at) " +
                "VALUES($game,$name,$path,$size,$mtime,$duration,0,'cloud',$rid,$uploaded)",
                ("$game", game), ("$name", remote.Name), ("$path", filepath),
                ("$size", remote.Size), ("$mtime", mtime), ("$duration", duration),
                ("$rid", remote.Id), ("$uploaded", RemoteUploadedAt(remote)));
            imported++;
        }

        tx.Commit();
        return new SyncResult(imported, updated, videoFiles.Count, skipped);
    }

    public static Task<bool> DownloadClipAsync(long clipId) => DownloadClipAsync(clipId, Connection);

    internal static async Task<bool> DownloadClipAsync(long clipId, CloudConnection connection)
    {
        using var con = Db.Open();
        var row = con.QueryOne(
            "SELECT filepath, storage, remote_id FROM clips WHERE id=$id", ("$id", clipId))
            ?? throw new FileNotFoundException(Strings.Get("clip.notFound"));
        if ((row["storage"] as string) != "cloud") return true;

        var filepath = row["filepath"] as string ?? "";
        var remoteId = row["remote_id"] as string;
        if (string.IsNullOrEmpty(filepath)) throw new InvalidOperationException(Strings.Get("cloud.noTargetPath"));
        if (string.IsNullOrEmpty(remoteId)) throw new InvalidOperationException(Strings.Get("cloud.noRemoteId"));

        var token = await connection.GetAccessTokenAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(filepath)!);

        string tmp = filepath + ".part";
        try
        {
            await connection.DownloadAsync(token, remoteId, tmp);
            if (File.Exists(filepath)) File.Delete(filepath);
            File.Move(tmp, filepath);
        }
        finally { if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } } }

        con.Exec("UPDATE clips SET storage='local', remote_id=NULL, remote_uploaded_at=NULL WHERE id=$id",
            ("$id", clipId));

        try { await connection.TrashAsync(token, remoteId); } catch { }
        connection.InvalidateAbout();
        return false;
    }

    public static Task<(int uploaded, int total, int skipped, int failed)> UploadFolderAsync(long folderId) => UploadFolderAsync(folderId, Connection);

    internal static async Task<(int uploaded, int total, int skipped, int failed)> UploadFolderAsync(long folderId, CloudConnection connection)
    {
        List<long> clipIds;
        using (var con = Db.Open())
        {
            var rows = con.Query("SELECT clip_id FROM folder_clips WHERE folder_id=$id", ("$id", folderId));
            clipIds = rows.Select(r => Convert.ToInt64(r["clip_id"])).ToList();
        }

        int uploaded = 0, skipped = 0, failed = 0;
        foreach (var id in clipIds)
        {
            try
            {
                if (await UploadClipAsync(id, connection)) skipped++;
                else uploaded++;
            }
            catch { failed++; }
        }
        return (uploaded, clipIds.Count, skipped, failed);
    }

    public static Task TrashRemoteAsync(string remoteId) => TrashRemoteAsync(remoteId, Connection);

    internal static async Task TrashRemoteAsync(string remoteId, CloudConnection connection)
    {
        if (!connection.IsConnected)
            throw new InvalidOperationException(Strings.Get("cloud.notConnected"));

        var token = await connection.GetAccessTokenAsync();
        await connection.TrashAsync(token, remoteId);
        connection.InvalidateAbout();
    }

    public static Task ProxyVideoAsync(HttpContext http, string remoteId) => ProxyVideoAsync(http, remoteId, Connection);

    internal static async Task ProxyVideoAsync(HttpContext http, string remoteId, CloudConnection connection)
    {
        string token;
        try { token = await connection.GetAccessTokenAsync(); }
        catch (Exception ex)
        {
            http.Response.StatusCode = 502;
            await http.Response.WriteAsJsonAsync(new { detail = ex.Message }, http.RequestAborted);
            return;
        }

        string? range = http.Request.Headers.TryGetValue("Range", out var rv) ? rv.ToString() : null;
        if (string.IsNullOrEmpty(range)) range = null;

        using var resp = await connection.OpenMediaAsync(token, remoteId, range, http.RequestAborted);

        http.Response.StatusCode = (int)resp.StatusCode;
        var rc = resp.Content.Headers;
        if (rc.ContentType is not null) http.Response.ContentType = rc.ContentType.ToString();
        if (rc.ContentLength is not null) http.Response.ContentLength = rc.ContentLength;
        if (rc.ContentRange is not null) http.Response.Headers["Content-Range"] = rc.ContentRange.ToString();
        http.Response.Headers["Accept-Ranges"] = "bytes";

        try
        {
            await using var s = await resp.Content.ReadAsStreamAsync(http.RequestAborted);
            await s.CopyToAsync(http.Response.Body, http.RequestAborted);
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<string> EnsureFolderAsync(string token, CloudConnection connection)
    {
        string? known = Settings.GetString(FolderIdSettingKey);
        string id = await connection.EnsureFolderAsync(token, Config.DriveFolderName, known);
        if (id != known) Settings.SetString(FolderIdSettingKey, id);
        return id;
    }

    private static string NowIso() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static double RemoteDuration(GoogleDrive.DriveFile file) =>
        file.AppProperties.TryGetValue("duration", out var value)
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
        && double.IsFinite(duration) && duration > 0 && duration <= TimeSpan.MaxValue.TotalSeconds
            ? duration
            : 0.0;

    private static double RemoteMtime(GoogleDrive.DriveFile file)
    {
        if (file.AppProperties.TryGetValue("mtime", out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mtime)
            && double.IsFinite(mtime) && mtime > 0 && mtime <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() / 1000.0)
            return mtime;
        var timestamp = file.CreatedTime ?? file.ModifiedTime ?? DateTimeOffset.UtcNow;
        return timestamp.ToUnixTimeMilliseconds() / 1000.0;
    }

    private static string RemoteUploadedAt(GoogleDrive.DriveFile file) =>
        (file.CreatedTime ?? file.ModifiedTime ?? DateTimeOffset.UtcNow)
        .ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    internal static string RemoteGame(GoogleDrive.DriveFile file)
    {
        if (file.AppProperties.TryGetValue("game", out var game) && !string.IsNullOrWhiteSpace(game))
            return game == "_root" ? game : DisplayHelper.SafeFolderName(game);

        var stem = Path.GetFileNameWithoutExtension(file.Name);
        if (stem.Length > 255) return "Chmura";
        Match match;
        try
        {
            match = Regex.Match(stem,
                @"^(?<game>.+?)\s+\d{4}-\d{2}-\d{2}[ _-]\d{2}[-:.]\d{2}[-:.]\d{2}",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException) { return "Chmura"; }
        if (!match.Success) return "Chmura";
        var detected = match.Groups["game"].Value.Trim();
        if (detected.Equals("cs2", StringComparison.OrdinalIgnoreCase))
            return "Counter-Strike 2";
        return DisplayHelper.SafeFolderName(detected);
    }

    private static string UniqueCloudPath(
        Microsoft.Data.Sqlite.SqliteConnection con, string game, string remoteName, string remoteId)
    {
        string root = Settings.GetClipsRoot();
        string directory = game == "_root" ? root : Path.Combine(root, DisplayHelper.SafeFolderName(game));
        string remoteExtension = Path.GetExtension(remoteName);
        string safeStem = DisplayHelper.SafeFolderName(Path.GetFileNameWithoutExtension(remoteName));
        string safeName = safeStem[..Math.Min(safeStem.Length, 80 - remoteExtension.Length)] + remoteExtension;
        string candidate = Path.Combine(directory, safeName);
        var owner = con.QueryOne("SELECT remote_id FROM clips WHERE filepath=$path", ("$path", candidate));
        if ((owner is null && !File.Exists(candidate)) || (owner?["remote_id"] as string) == remoteId)
            return candidate;

        string extension = Path.GetExtension(safeName);
        string stem = Path.GetFileNameWithoutExtension(safeName);
        string suffix = DisplayHelper.SanitizeFileName(remoteId.Length > 8 ? remoteId[..8] : remoteId);
        for (int number = 1; ; number++)
        {
            string counter = number == 1 ? "" : $"_{number}";
            candidate = Path.Combine(directory, $"{stem}_cloud_{suffix}{counter}{extension}");
            owner = con.QueryOne("SELECT remote_id FROM clips WHERE filepath=$path", ("$path", candidate));
            if ((owner is null && !File.Exists(candidate)) || (owner?["remote_id"] as string) == remoteId)
                return candidate;
        }
    }

    private static string VideoMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        _ => "video/mp4",
    };
}
