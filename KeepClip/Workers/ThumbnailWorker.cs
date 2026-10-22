namespace KeepClip.Workers;

public static class ThumbnailWorker
{
    private static readonly object Lock = new();
    private static Task? _task;

    public static void Ensure()
        => Ensure(new ClipOperations());

    internal static Task? Completion { get { lock (Lock) return _task; } }

    internal static void Ensure(ClipOperations operations)
    {
        lock (Lock)
        {
            if (_task is { IsCompleted: false }) return;
            _task = Task.Run(() => Work(operations));
        }
    }

    private static void Work(ClipOperations operations)
    {
        long lastId = 0;

        while (true)
        {
            long cid;
            string filepath;
            object? durationObj;

            using (var con = Db.Open())
            {
                var row = con.QueryOne(
                    "SELECT id, filepath, duration FROM clips WHERE id > $after AND (has_thumb=0 OR duration IS NULL) ORDER BY id LIMIT 1", ("$after", lastId));
                if (row is null) return;
                cid = Convert.ToInt64(row["id"]);
                filepath = (string)row["filepath"]!;
                durationObj = row["duration"];
            }

            lastId = cid;

            if (!File.Exists(filepath))
            {
                using var con = Db.Open();
                con.Exec("UPDATE clips SET has_thumb=2 WHERE id=$id", ("$id", cid));
                continue;
            }

            double? newDuration = durationObj is null
                ? operations.ProbeDuration(filepath)
                : Convert.ToDouble(durationObj);

            int hasThumbNow = 1;
            if (!File.Exists(Config.ThumbPath(cid)))
                hasThumbNow = operations.MakeThumbnail(cid, filepath) ? 1 : 2;

            using (var con = Db.Open())
            {
                con.Exec(
                    "UPDATE clips SET has_thumb=$h, duration=COALESCE($d, duration) WHERE id=$id",
                    ("$h", hasThumbNow), ("$d", newDuration), ("$id", cid));
            }
        }
    }
}
