namespace KeepClip.Workers;

public static class ThumbnailWorker
{
    private static readonly object Lock = new();
    private static Task? _task;

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
        // Uszkodzony plik może stale zwracać brak czasu trwania i zapętlić wybieranie tego
        // samego rekordu, dlatego jeden przebieg próbuje każdy identyfikator tylko raz.
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

            if (!attempted.Add(cid)) return; // Kolejna próba tego rekordu nie przyniosłaby postępu.

            if (!File.Exists(filepath))
            {
                using var con = Db.Open();
                con.Exec("UPDATE clips SET has_thumb=2 WHERE id=$id", ("$id", cid));
                continue;
            }

            double? newDuration = durationObj is null
                ? Media.ProbeDuration(filepath)
                : Convert.ToDouble(durationObj);

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
