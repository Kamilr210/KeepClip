namespace KeepClip.Workers;

public static class Scanner
{
    private static readonly object ScanLock = new();

    public static Dictionary<string, object?> Scan() => Scan(CancellationToken.None);

    public static Dictionary<string, object?> Scan(CancellationToken cancellationToken) => Scan(cancellationToken, LibraryTraversal.MaximumEntries, TimeSpan.FromSeconds(30));

    internal static Dictionary<string, object?> Scan(CancellationToken cancellationToken, int maximumEntries, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        bool entered = false;
        try
        {
            while (!(entered = Monitor.TryEnter(ScanLock, 50))) deadline.Token.ThrowIfCancellationRequested();
            deadline.Token.ThrowIfCancellationRequested();
            return ScanCore(deadline.Token, maximumEntries);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            return new() { ["found"] = 0, ["added"] = 0, ["removed"] = 0, ["updated"] = 0, ["error"] = ex is OperationCanceledException ? "Skanowanie anulowano lub przekroczono limit czasu." : ex.Message };
        }
        finally { if (entered) Monitor.Exit(ScanLock); }
    }

    private static Dictionary<string, object?> ScanCore(CancellationToken cancellationToken, int maximumEntries)
    {
        int found = 0, added = 0, removed = 0, updated = 0;
        var clipsRoot = Settings.GetClipsRoot();

        if (!Directory.Exists(clipsRoot))
        {
            return new()
            {
                ["found"] = 0, ["added"] = 0, ["removed"] = 0, ["updated"] = 0,
                ["error"] = $"Folder z klipami nie istnieje: {clipsRoot}",
            };
        }

        var clipsRootNorm = NormDir(clipsRoot);

        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var settledOnDisk = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        var settledBefore = DateTime.UtcNow.AddMilliseconds(-1500);
        foreach (var file in LibraryTraversal.Files(clipsRoot, cancellationToken, maximumEntries))
        {
            var path = file.FullName;
            if (Config.VideoExts.Contains(Path.GetExtension(path)))
            {
                onDisk.Add(path);
                if (file.LastWriteTimeUtc <= settledBefore)
                    settledOnDisk[path] = file;
            }
        }
        found = onDisk.Count;
        cancellationToken.ThrowIfCancellationRequested();

        using var con = Db.Open();
        using var tx = con.BeginTransaction();

        var deadIds = new List<long>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingLocal = new Dictionary<string, (long Id, long Size, double Mtime)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in con.Query("SELECT id, filepath, storage, size_bytes, mtime FROM clips"))
        {
            var fp = (string)row["filepath"]!;
            var storage = row["storage"] as string;
            if (storage == "cloud") { existingPaths.Add(fp); continue; }
            if (onDisk.Contains(fp))
            {
                existingPaths.Add(fp);
                existingLocal[fp] = (
                    Convert.ToInt64(row["id"]),
                    Convert.ToInt64(row["size_bytes"] ?? 0L),
                    Convert.ToDouble(row["mtime"] ?? 0.0));
            }
            else deadIds.Add(Convert.ToInt64(row["id"]));
        }

        var thumbnailsToDelete = new List<string>();
        foreach (var cid in deadIds)
        {
            thumbnailsToDelete.Add(Config.ThumbPath(cid));
            con.Exec("DELETE FROM clips WHERE id=$id", ("$id", cid));
            removed++;
        }

        foreach (var (spath, fi) in settledOnDisk)
        {
            long size = fi.Length;
            double mtime = ((DateTimeOffset)fi.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;

            if (existingLocal.TryGetValue(spath, out var existing))
            {
                if (existing.Size != size || Math.Abs(existing.Mtime - mtime) > 0.001)
                {
                    thumbnailsToDelete.Add(Config.ThumbPath(existing.Id));
                    con.Exec("UPDATE clips SET size_bytes=$s, mtime=$m, has_thumb=0, duration=NULL, thumb_retry_at=NULL WHERE id=$id",
                        ("$s", size), ("$m", mtime), ("$id", existing.Id));
                    updated++;
                }
                continue;
            }
            if (existingPaths.Contains(spath)) continue;

            var parent = Path.GetDirectoryName(spath)!;
            var game = NormDir(parent) == clipsRootNorm ? "_root" : new DirectoryInfo(parent).Name;
            con.Exec(
                "INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES($g,$f,$p,$s,$m)",
                ("$g", game), ("$f", Path.GetFileName(spath)), ("$p", spath),
                ("$s", size), ("$m", mtime));
            added++;
        }

        tx.Commit();
        foreach (var tp in thumbnailsToDelete)
        {
            try { if (File.Exists(tp)) File.Delete(tp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return new() { ["found"] = found, ["added"] = added, ["removed"] = removed, ["updated"] = updated, ["pending"] = found - settledOnDisk.Count };
    }

    private static string NormDir(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}