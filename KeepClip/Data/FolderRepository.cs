namespace KeepClip.Data;

public enum FolderWrite { Ok, NotFound, Duplicate }

public enum AddClipResult { Ok, NoFolder, NoClip }

public class FolderRepository
{
    private static readonly Dictionary<string, string> ClipSorts = new()
    {
        ["newest"] = "c.mtime DESC",
        ["oldest"] = "c.mtime ASC",
        ["largest"] = "c.size_bytes DESC",
        ["smallest"] = "c.size_bytes ASC",
        ["added"] = "fc.added_at DESC",
    };

    public List<Dictionary<string, object?>> List()
    {
        using var con = Db.Open();
        var folders = con.Query(
            @"SELECT f.id, f.name, f.created_at, f.hidden_from_home,
                     (SELECT COUNT(*) FROM folder_clips fc WHERE fc.folder_id = f.id) AS clip_count
              FROM folders f ORDER BY f.name COLLATE NOCASE");
        foreach (var f in folders)
        {
            f["hidden_from_home"] = Convert.ToInt64(f["hidden_from_home"]) != 0;
            var ids = con.Query(
                "SELECT clip_id FROM folder_clips WHERE folder_id=$id ORDER BY added_at DESC LIMIT 4",
                ("$id", f["id"]));
            f["sample_clip_ids"] = ids.Select(r => r["clip_id"]).ToList();
        }
        return folders;
    }

    public Dictionary<string, object?>? Create(string name)
    {
        using var con = Db.Open();
        try { con.Exec("INSERT INTO folders(name) VALUES($n)", ("$n", name)); }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) { return null; }
        var id = con.ScalarLong("SELECT last_insert_rowid()");
        var folder = con.QueryOne("SELECT id, name, created_at, hidden_from_home FROM folders WHERE id=$id", ("$id", id));
        if (folder is not null) folder["hidden_from_home"] = Convert.ToInt64(folder["hidden_from_home"]) != 0;
        return folder;
    }

    public FolderWrite Rename(long id, string name)
    {
        using var con = Db.Open();
        if (con.QueryOne("SELECT id FROM folders WHERE id=$id", ("$id", id)) is null) return FolderWrite.NotFound;
        try { con.Exec("UPDATE folders SET name=$n WHERE id=$id", ("$n", name), ("$id", id)); }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19) { return FolderWrite.Duplicate; }
        return FolderWrite.Ok;
    }

    public string? Delete(long id)
    {
        using var con = Db.Open();
        var row = con.QueryOne("SELECT name FROM folders WHERE id=$id", ("$id", id));
        if (row is null) return null;
        con.Exec("DELETE FROM folders WHERE id=$id", ("$id", id));
        return row["name"] as string;
    }

    public bool SetHiddenFromHome(long id, bool hiddenFromHome)
    {
        using var con = Db.Open();
        return con.Exec("UPDATE folders SET hidden_from_home=$hidden WHERE id=$id", ("$hidden", hiddenFromHome ? 1 : 0), ("$id", id)) > 0;
    }

    public (Dictionary<string, object?> folder, List<Dictionary<string, object?>> clips)? ListClips(long folderId, string? sort, int limit)
    {
        using var con = Db.Open();
        var folder = con.QueryOne("SELECT id, name, created_at, hidden_from_home FROM folders WHERE id=$id", ("$id", folderId));
        if (folder is null) return null;
        folder["hidden_from_home"] = Convert.ToInt64(folder["hidden_from_home"]) != 0;
        var order = ClipSorts.GetValueOrDefault(sort ?? "newest", ClipSorts["added"]);
        var clips = con.Query(
            $@"SELECT c.id, c.game, c.filename, c.duration, c.size_bytes, c.mtime,
                      c.has_thumb, c.transcribed_at, c.storage
               FROM folder_clips fc JOIN clips c ON c.id = fc.clip_id
               WHERE fc.folder_id = $id ORDER BY {order} LIMIT $limit",
            ("$id", folderId), ("$limit", limit));
        return (folder, clips);
    }

    public AddClipResult AddClip(long folderId, long clipId)
    {
        using var con = Db.Open();
        if (con.QueryOne("SELECT 1 FROM folders WHERE id=$id", ("$id", folderId)) is null) return AddClipResult.NoFolder;
        if (con.QueryOne("SELECT 1 FROM clips WHERE id=$id", ("$id", clipId)) is null) return AddClipResult.NoClip;
        con.Exec("INSERT OR IGNORE INTO folder_clips(folder_id, clip_id) VALUES($f,$c)",
            ("$f", folderId), ("$c", clipId));
        return AddClipResult.Ok;
    }

    public void RemoveClip(long folderId, long clipId)
    {
        using var con = Db.Open();
        con.Exec("DELETE FROM folder_clips WHERE folder_id=$f AND clip_id=$c", ("$f", folderId), ("$c", clipId));
    }

    public List<long> GetFolderIdsForClip(long clipId)
    {
        using var con = Db.Open();
        return con.Query("SELECT folder_id FROM folder_clips WHERE clip_id=$id", ("$id", clipId))
            .Select(r => Convert.ToInt64(r["folder_id"])).ToList();
    }
}
