namespace KeepClip.Data;

/// <summary>Segments (transcript lines) access, plus the FTS search query.</summary>
public class SegmentRepository
{
    private static readonly Dictionary<string, string> SearchSorts = new()
    {
        ["relevance"] = "score ASC",
        ["newest"] = "c.mtime DESC, score ASC",
        ["oldest"] = "c.mtime ASC, score ASC",
        ["largest"] = "c.size_bytes DESC, score ASC",
        ["smallest"] = "c.size_bytes ASC, score ASC",
    };

    /// <summary>A clip's transcript lines (raw rows, ordered by start time).</summary>
    public List<Dictionary<string, object?>> ListByClipId(long clipId)
    {
        using var con = Db.Open();
        return con.Query(
            "SELECT id, start_s, end_s, text FROM segments WHERE clip_id=$id ORDER BY start_s",
            ("$id", clipId));
    }

    /// <summary>True if the segment existed and its text was updated.</summary>
    public bool UpdateText(long segmentId, string text)
    {
        using var con = Db.Open();
        if (con.QueryOne("SELECT id FROM segments WHERE id=$id", ("$id", segmentId)) is null)
            return false;
        con.Exec("UPDATE segments SET text=$t WHERE id=$id", ("$t", text), ("$id", segmentId));
        return true;
    }

    public void DeleteByClipId(long clipId)
    {
        using var con = Db.Open();
        con.Exec("DELETE FROM segments WHERE clip_id=$id", ("$id", clipId));
    }

    public void Insert(long clipId, double startS, double endS, string text)
    {
        using var con = Db.Open();
        con.Exec("INSERT INTO segments (clip_id, start_s, end_s, text) VALUES ($c,$s,$e,$t)",
            ("$c", clipId), ("$s", startS), ("$e", endS), ("$t", text));
    }

    /// <summary>Replace all of a clip's segments in one transaction (used by retranscribe).</summary>
    public void ReplaceAll(long clipId, IEnumerable<(double Start, double End, string Text)> segs)
    {
        using var con = Db.Open();
        using var tx = con.BeginTransaction();
        con.Exec("DELETE FROM segments WHERE clip_id=$id", ("$id", clipId));
        foreach (var s in segs)
            con.Exec("INSERT INTO segments (clip_id, start_s, end_s, text) VALUES ($c,$s,$e,$t)",
                ("$c", clipId), ("$s", s.Start), ("$e", s.End), ("$t", s.Text));
        tx.Commit();
    }

    /// <summary>Shift a clip's segments by <paramref name="offset"/> seconds, dropping any that
    /// fall entirely before zero. Returns (shifted, dropped) counts. Used after a fix-trim.</summary>
    public (int shifted, int dropped) ShiftTimestamps(long clipId, double offset)
    {
        using var con = Db.Open();
        int shifted = 0, dropped = 0;
        foreach (var s in con.Query("SELECT id, start_s, end_s FROM segments WHERE clip_id=$id", ("$id", clipId)))
        {
            var sid = Convert.ToInt64(s["id"]);
            var newStart = Convert.ToDouble(s["start_s"]) - offset;
            var newEnd = Convert.ToDouble(s["end_s"]) - offset;
            if (newEnd <= 0)
            {
                con.Exec("DELETE FROM segments WHERE id=$id", ("$id", sid));
                dropped++;
            }
            else
            {
                con.Exec("UPDATE segments SET start_s=$st, end_s=$en WHERE id=$id",
                    ("$st", Math.Max(0.0, newStart)), ("$en", newEnd), ("$id", sid));
                shifted++;
            }
        }
        return (shifted, dropped);
    }

    /// <summary>Full-text search hits (raw rows) for the already-built FTS expression.</summary>
    public List<Dictionary<string, object?>> Search(string ftsQuery, string? sort, int limit)
    {
        var order = SearchSorts.GetValueOrDefault(sort ?? "relevance", SearchSorts["relevance"]);
        using var con = Db.Open();
        return con.Query(
            $@"SELECT s.id AS segment_id, s.start_s, s.end_s, s.text,
                      c.id AS clip_id, c.game, c.filename, c.duration,
                      c.size_bytes, c.mtime, c.has_thumb, c.favorite, c.storage,
                      snippet(segments_fts, 0, '<mark>', '</mark>', '...', 12) AS snippet,
                      bm25(segments_fts) AS score
               FROM segments_fts
               JOIN segments s ON s.id = segments_fts.rowid
               JOIN clips c ON c.id = s.clip_id
               WHERE segments_fts MATCH $q
               ORDER BY {order}
               LIMIT $limit",
            ("$q", ftsQuery), ("$limit", limit));
    }
}
