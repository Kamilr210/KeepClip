using System.Globalization;

namespace KeepClip.Services;

public static class CloudService
{
    private const string FolderIdSettingKey = "cloud_folder_id";

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

        // Lokalny plik trafia do Kosza, ale miniatura zostaje do podglądu klipu w chmurze.
        Trash.SendWithRetry(filepath);
        OAuthService.InvalidateAbout();
        return false;
    }

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

        // Pobieranie do pliku tymczasowego chroni ścieżkę docelową przed niepełnym plikiem.
        string tmp = filepath + ".part";
        try
        {
            await GoogleDrive.DownloadAsync(token, remoteId, tmp);
            if (File.Exists(filepath)) File.Delete(filepath);
            File.Move(tmp, filepath);
        }
        finally { if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } } }

        con.Exec("UPDATE clips SET storage='local', remote_id=NULL, remote_uploaded_at=NULL WHERE id=$id",
            ("$id", clipId));

// Plik jest już bezpiecznie lokalnie, więc błąd Kosza Dysku Google nie może cofnąć operacji.
        try { await GoogleDrive.TrashAsync(token, remoteId); } catch { }
        OAuthService.InvalidateAbout();
        return false;
    }

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

    public static async Task TryTrashRemoteAsync(string remoteId)
    {
        if (!OAuthService.IsConfigured() || !OAuthService.IsConnected()) return;
        try
        {
            var token = await OAuthService.GetAccessTokenAsync();
            await GoogleDrive.TrashAsync(token, remoteId);
        }
        catch { }
    }

    // Nagłówek HTTP Range i odpowiedź 206 są przekazywane, aby umożliwić przewijanie.
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
        catch (OperationCanceledException) { }
    }


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
