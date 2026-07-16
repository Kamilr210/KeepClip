namespace KeepClip.Workers;

// Klipy przeniesione do chmury nie są usuwane z bazy, a ich ścieżki pozostają
// zarezerwowane, aby ponowne pojawienie się pliku nie utworzyło duplikatu.
public static class Scanner
{
    public static Dictionary<string, object?> Scan()
    {
        Db.InitDb();
        int found = 0, added = 0, removed = 0;
        var clipsRoot = Settings.GetClipsRoot();

        if (!Directory.Exists(clipsRoot))
        {
            return new()
            {
                ["found"] = 0, ["added"] = 0, ["removed"] = 0,
                ["error"] = $"Folder z klipami nie istnieje: {clipsRoot}",
            };
        }

        var clipsRootNorm = NormDir(clipsRoot);

        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(clipsRoot, "*", SearchOption.AllDirectories))
        {
            if (Config.VideoExts.Contains(Path.GetExtension(path)))
                onDisk.Add(path);
        }
        found = onDisk.Count;

        using var con = Db.Open();
        using var tx = con.BeginTransaction();

        con.Exec("UPDATE clips SET has_thumb=0 WHERE has_thumb=2");

        var deadIds = new List<long>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in con.Query("SELECT id, filepath, storage FROM clips"))
        {
            var fp = (string)row["filepath"]!;
            var storage = row["storage"] as string;
            if (storage == "cloud") { existingPaths.Add(fp); continue; }
            if (onDisk.Contains(fp)) existingPaths.Add(fp);
            else deadIds.Add(Convert.ToInt64(row["id"]));
        }

        foreach (var cid in deadIds)
        {
            var tp = Config.ThumbPath(cid);
            try { if (File.Exists(tp)) File.Delete(tp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            con.Exec("DELETE FROM clips WHERE id=$id", ("$id", cid)); // Usunięcie kaskadowe obejmuje segmenty.
            removed++;
        }

        foreach (var spath in onDisk)
        {
            if (existingPaths.Contains(spath)) continue;
            var parent = Path.GetDirectoryName(spath)!;
            var game = NormDir(parent) == clipsRootNorm ? "_root" : new DirectoryInfo(parent).Name;
            var fi = new FileInfo(spath);
            long size = fi.Length;
            double mtime = ((DateTimeOffset)File.GetLastWriteTimeUtc(spath)).ToUnixTimeMilliseconds() / 1000.0;
            con.Exec(
                "INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES($g,$f,$p,$s,$m)",
                ("$g", game), ("$f", Path.GetFileName(spath)), ("$p", spath),
                ("$s", size), ("$m", mtime));
            added++;
        }

        tx.Commit();
        return new() { ["found"] = found, ["added"] = added, ["removed"] = removed };
    }

    private static string NormDir(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
