namespace KeepClip;

/// <summary>
/// Background generation of missing thumbnails + duration probing — a port of
/// app.py's <c>_thumbnail_worker</c> / <c>_ensure_thumbnail_worker</c>. A single
/// background task drains clips <c>WHERE has_thumb=0 OR duration IS NULL</c>,
/// started after every scan/config change and never more than one at a time.
/// </summary>
public static class ThumbnailWorker
{
    private static readonly object Lock = new();
    private static Task? _task;

    /// <summary>Start the worker if it isn't already running (mirrors _ensure_thumbnail_worker).</summary>
    public static void Ensure()
    {
        lock (Lock)
        {
            if (_task is { IsCompleted: false }) return;
            _task = Task.Run(Work);
        }
    }

    private static void Work()
    {
        // Guard against a pathological busy-loop: if a clip's file exists but its
        // duration never resolves (ffprobe can't read a truncated container), the
        // `duration IS NULL` predicate would re-select it forever. The Python source
        // has this latent risk; we break out once we re-encounter an id we already
        // attempted this pass. Normal clips resolve on the first try, so behaviour
        // is identical in every real case.
        var attempted = new HashSet<long>();

        while (true)
        {
            long cid;
            string filepath;
            object? durationObj;

            using (var con = Db.Open())
            {
                var row = con.QueryOne(
                    "SELECT id, filepath, duration FROM clips WHERE has_thumb=0 OR duration IS NULL LIMIT 1");
                if (row is null) return;
                cid = Convert.ToInt64(row["id"]);
                filepath = (string)row["filepath"]!;
                durationObj = row["duration"];
            }

            if (!attempted.Add(cid)) return; // already tried this one — no progress possible

            if (!File.Exists(filepath))
            {
                using var con = Db.Open();
                con.Exec("UPDATE clips SET has_thumb=2 WHERE id=$id", ("$id", cid));
                continue;
            }

            // Fill in duration if missing — needed for the hover-preview start offset.
            double? newDuration = durationObj is null
                ? Media.ProbeDuration(filepath)
                : Convert.ToDouble(durationObj);

            // Generate thumbnail if missing.
            int hasThumbNow = 1;
            if (!File.Exists(Config.ThumbPath(cid)))
                hasThumbNow = Media.MakeThumbnail(cid, filepath) ? 1 : 2;

            using (var con = Db.Open())
            {
                con.Exec(
                    "UPDATE clips SET has_thumb=$h, duration=COALESCE($d, duration) WHERE id=$id",
                    ("$h", hasThumbNow), ("$d", newDuration), ("$id", cid));
            }
        }
    }
}
