namespace KeepClip.Data;

public class ClipRepository
{
    private static readonly Dictionary<string, string> ListSorts = new()
    {
        ["newest"] = "mtime DESC",
        ["oldest"] = "mtime ASC",
        ["largest"] = "size_bytes DESC",
        ["smallest"] = "size_bytes ASC",
    };

    public List<Dictionary<string, object?>> List(string? game, string? sort, int limit, bool favoritesOnly)
    {
        var order = ListSorts.GetValueOrDefault(sort ?? "newest", ListSorts["newest"]);
        var sql = "SELECT id, game, filename, duration, size_bytes, mtime, has_thumb, " +
                  "transcribed_at, favorite, storage FROM clips";
        var where = new List<string>();
        var ps = new List<(string, object?)>();
        if (!string.IsNullOrEmpty(game)) { where.Add("game = $game"); ps.Add(("$game", game)); }
        if (favoritesOnly) where.Add("favorite = 1");
        if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
        sql += $" ORDER BY {order} LIMIT $limit";
        ps.Add(("$limit", limit));
        using var con = Db.Open();
        return con.Query(sql, ps.ToArray());
    }

    public Dictionary<string, object?>? GetRow(long id)
    {
        using var con = Db.Open();
        return con.QueryOne("SELECT * FROM clips WHERE id=$id", ("$id", id));
    }

    public Clip? GetById(long id)
    {
        using var con = Db.Open();
        var row = con.QueryOne(
            "SELECT id, game, filename, duration, size_bytes, mtime, has_thumb, " +
            "transcribed_at, favorite, storage, remote_id, filepath FROM clips WHERE id=$id",
            ("$id", id));
        return row is null ? null : Map(row);
    }

    public long Count()
    {
        using var con = Db.Open();
        return con.ScalarLong("SELECT COUNT(*) FROM clips");
    }

    public bool? ToggleFavorite(long id)
    {
        using var con = Db.Open();
        var row = con.QueryOne("SELECT favorite FROM clips WHERE id = $id", ("$id", id));
        if (row is null) return null;
        var newState = Convert.ToInt64(row["favorite"]) != 0 ? 0 : 1;
        con.Exec("UPDATE clips SET favorite = $f WHERE id = $id", ("$f", newState), ("$id", id));
        return newState != 0;
    }

    public long Insert(string game, string filename, string filepath, long sizeBytes, double mtime)
    {
        using var con = Db.Open();
        con.Exec("INSERT INTO clips(game, filename, filepath, size_bytes, mtime) VALUES($g,$f,$p,$s,$m)",
            ("$g", game), ("$f", filename), ("$p", filepath), ("$s", sizeBytes), ("$m", mtime));
        return con.ScalarLong("SELECT last_insert_rowid()");
    }

    public void UpdateAfterFix(long id, long sizeBytes, double mtime, double? duration, int hasThumb)
    {
        using var con = Db.Open();
        con.Exec("UPDATE clips SET size_bytes=$sz, mtime=$mt, duration=$du, has_thumb=$ht WHERE id=$id",
            ("$sz", sizeBytes), ("$mt", mtime), ("$du", duration), ("$ht", hasThumb), ("$id", id));
    }

    public void SetTranscribed(long id, string timestamp, string language)
    {
        using var con = Db.Open();
        con.Exec("UPDATE clips SET transcribed_at=$ts, language=$lang WHERE id=$id",
            ("$ts", timestamp), ("$lang", language), ("$id", id));
    }

    public void SetThumbState(long id, int hasThumb)
    {
        using var con = Db.Open();
        con.Exec("UPDATE clips SET has_thumb=$ht WHERE id=$id", ("$ht", hasThumb), ("$id", id));
    }

    public void Delete(long id)
    {
        using var con = Db.Open();
        con.Exec("DELETE FROM clips WHERE id=$id", ("$id", id));
    }

    private static Clip Map(Dictionary<string, object?> r) => new(
        Convert.ToInt64(r["id"]),
        r["game"] as string ?? "",
        r["filename"] as string ?? "",
        Convert.ToDouble(r["duration"] ?? 0.0),
        Convert.ToInt64(r["size_bytes"] ?? 0L),
        Convert.ToDouble(r["mtime"] ?? 0.0),
        Convert.ToInt32(r["has_thumb"] ?? 0L),
        r["transcribed_at"] as string,
        Convert.ToInt32(r["favorite"] ?? 0L),
        r["storage"] as string,
        r.GetValueOrDefault("remote_id") as string,
        r.GetValueOrDefault("filepath") as string
    );
}
