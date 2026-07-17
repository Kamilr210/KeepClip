namespace KeepClip.Workers;

// Klipy przeniesione do chmury nie są usuwane z bazy, a ich ścieżki pozostają
// zarezerwowane, aby ponowne pojawienie się pliku nie utworzyło duplikatu.
public static class Scanner
{
    private static readonly object ScanLock = new();

    public static Dictionary<string, object?> Scan()
    {
        lock (ScanLock)
            return ScanCore();
    }

    private static Dictionary<string, object?> ScanCore()
    {
        Db.InitDb();
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
        // Krótkie opóźnienie zapobiega dodaniu pliku, który ShadowPlay lub ffmpeg nadal zapisuje.
        var settledOnDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settledBefore = DateTime.UtcNow.AddMilliseconds(-1500);
        foreach (var path in Directory.EnumerateFiles(clipsRoot, "*", SearchOption.AllDirectories))
        {
            if (Config.VideoExts.Contains(Path.GetExtension(path)))
            {
                onDisk.Add(path);
                if (File.GetLastWriteTimeUtc(path) <= settledBefore)
                    settledOnDisk.Add(path);
            }
        }
        found = onDisk.Count;

        using var con = Db.Open();
        using var tx = con.BeginTransaction();

        con.Exec("UPDATE clips SET has_thumb=0 WHERE has_thumb=2");

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

        foreach (var cid in deadIds)
        {
            var tp = Config.ThumbPath(cid);
            try { if (File.Exists(tp)) File.Delete(tp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            con.Exec("DELETE FROM clips WHERE id=$id", ("$id", cid)); // Usunięcie kaskadowe obejmuje segmenty.
            removed++;
        }

        foreach (var spath in settledOnDisk)
        {
            var fi = new FileInfo(spath);
            long size = fi.Length;
            double mtime = ((DateTimeOffset)File.GetLastWriteTimeUtc(spath)).ToUnixTimeMilliseconds() / 1000.0;

            if (existingLocal.TryGetValue(spath, out var existing))
            {
                if (existing.Size != size || Math.Abs(existing.Mtime - mtime) > 0.001)
                {
                    var tp = Config.ThumbPath(existing.Id);
                    try { if (File.Exists(tp)) File.Delete(tp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    con.Exec("UPDATE clips SET size_bytes=$s, mtime=$m, has_thumb=0 WHERE id=$id",
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
        return new() { ["found"] = found, ["added"] = added, ["removed"] = removed, ["updated"] = updated };
    }

    private static string NormDir(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
