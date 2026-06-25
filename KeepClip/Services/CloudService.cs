using System.Globalization;

namespace KeepClip.Services;

/// <summary>
/// Google Drive offload orchestration on top of <see cref="OAuthService"/> (auth/tokens)
/// and the low-level <see cref="GoogleDrive"/> REST client: per-clip upload/download/trash,
/// folder bulk-upload, and the streaming video proxy that lets cloud clips play back in the
/// browser's &lt;video&gt; element. All token/account state lives in OAuthService.
/// </summary>
public static class CloudService
{
    private const string FolderIdSettingKey = "cloud_folder_id";

    /// <summary>
    /// Offload a local clip to Drive: upload the file, flip the row to <c>storage='cloud'</c>
    /// with its <c>remote_id</c>, then send the local file to the Recycle Bin (recoverable).
    /// Returns true if the clip was already in the cloud (no-op).
    /// </summary>
    public static async Task<bool> UploadClipAsync(long clipId)
    {
        using var con = Db.Open();
        var row = con.QueryOne(
            "SELECT filepath, filename, storage FROM clips WHERE id=$id", ("$id", clipId))
            ?? throw new FileNotFoundException("Nie znaleziono klipu.");
        if ((row["storage"] as string) == "cloud") return true;

        var filepath = row["filepath"] as string ?? "";
        var filename = row["filename"] as string ?? Path.GetFileName(filepath);
        if (string.IsNullOrEmpty(filepath) || !File.Exists(filepath))
            throw new FileNotFoundException("Plik nie istnieje na dysku.");

        var token = await OAuthService.GetAccessTokenAsync();
        var folderId = await EnsureFolderAsync(token);
        string remoteId = await GoogleDrive.UploadResumableAsync(
            token, filename, folderId, filepath, VideoMime(filepath));

        con.Exec("UPDATE clips SET storage='cloud', remote_id=$rid, remote_uploaded_at=$ts WHERE id=$id",
            ("$rid", remoteId), ("$ts", NowIso()), ("$id", clipId));

        // Local copy → Recycle Bin (recoverable), the whole point of "offload". The cached
        // thumbnail is kept so the cloud clip still shows a preview in the grid.
        Trash.SendWithRetry(filepath);
        OAuthService.InvalidateAbout();
        return false;
    }

    /// <summary>
    /// Bring a cloud clip back to disk: download it to its original path, flip the row back
    /// to <c>storage='local'</c>, then move the Drive copy to Drive trash (recoverable).
    /// Returns true if the clip was already local (no-op).
    /// </summary>
    public static async Task<bool> DownloadClipAsync(long clipId)
    {
        using var con = Db.Open();
        var row = con.QueryOne(
            "SELECT filepath, storage, remote_id FROM clips WHERE id=$id", ("$id", clipId))
            ?? throw new FileNotFoundException("Nie znaleziono klipu.");
        if ((row["storage"] as string) != "cloud") return true;

        var filepath = row["filepath"] as string ?? "";
        var remoteId = row["remote_id"] as string;
        if (string.IsNullOrEmpty(filepath)) throw new InvalidOperationException("Brak ścieżki docelowej klipu.");
        if (string.IsNullOrEmpty(remoteId)) throw new InvalidOperationException("Klip nie ma identyfikatora w chmurze.");

        var token = await OAuthService.GetAccessTokenAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(filepath)!);

        // Download to a sibling temp file, then swap into place so a failed/aborted transfer
        // never leaves a half-written clip at the real path.
        string tmp = filepath + ".part";
        try
        {
            await GoogleDrive.DownloadAsync(token, remoteId, tmp);
            if (File.Exists(filepath)) File.Delete(filepath);
            File.Move(tmp, filepath);
        }
        finally { if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* leftover temp */ } } }

        con.Exec("UPDATE clips SET storage='local', remote_id=NULL, remote_uploaded_at=NULL WHERE id=$id",
            ("$id", clipId));

        // Best-effort: the file is safely on disk now, so a trash failure only leaves a
        // recoverable orphan in the user's Drive.
        try { await GoogleDrive.TrashAsync(token, remoteId); } catch { /* user can clear it */ }
        OAuthService.InvalidateAbout();
        return false;
    }

    /// <summary>Upload every local clip in a user folder. Returns a tally for the toast.</summary>
    public static async Task<(int uploaded, int total, int skipped, int failed)> UploadFolderAsync(long folderId)
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
                if (await UploadClipAsync(id)) skipped++;
                else uploaded++;
            }
            catch { failed++; }
        }
        return (uploaded, clipIds.Count, skipped, failed);
    }

    /// <summary>Best-effort trash of a clip's Drive copy when the row itself is being deleted.</summary>
    public static async Task TryTrashRemoteAsync(string remoteId)
    {
        if (!OAuthService.IsConfigured() || !OAuthService.IsConnected()) return;
        try
        {
            var token = await OAuthService.GetAccessTokenAsync();
            await GoogleDrive.TrashAsync(token, remoteId);
        }
        catch { /* deletion already succeeded locally; the Drive copy is user-recoverable */ }
    }

    /// <summary>
    /// Relay a cloud clip's bytes from Drive to the HTTP response, forwarding the browser's
    /// Range header (and Drive's 206/Content-Range back) so the &lt;video&gt; element can seek.
    /// </summary>
    public static async Task ProxyVideoAsync(HttpContext http, string remoteId)
    {
        string token;
        try { token = await OAuthService.GetAccessTokenAsync(); }
        catch (Exception ex)
        {
            http.Response.StatusCode = 502;
            await http.Response.WriteAsJsonAsync(new { detail = ex.Message });
            return;
        }

        string? range = http.Request.Headers.TryGetValue("Range", out var rv) ? rv.ToString() : null;
        if (string.IsNullOrEmpty(range)) range = null;

        using var resp = await GoogleDrive.OpenMediaAsync(token, remoteId, range, http.RequestAborted);

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
        catch (OperationCanceledException) { /* client seeked or closed the player — normal */ }
    }

    // ---- helpers ----

    private static async Task<string> EnsureFolderAsync(string token)
    {
        string? known = Settings.GetString(FolderIdSettingKey);
        string id = await GoogleDrive.EnsureFolderAsync(token, Config.DriveFolderName, known);
        if (id != known) Settings.SetString(FolderIdSettingKey, id);
        return id;
    }

    private static string NowIso() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

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
