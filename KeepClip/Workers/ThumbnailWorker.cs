namespace KeepClip.Workers;

public static class ThumbnailWorker
{
    private static readonly object Lock = new();
    private static readonly SemaphoreSlim Queue = new(1, 1);
    private static Task? _task;

    public static void Ensure() => Ensure(new ClipOperations());
    internal static Task? Completion { get { lock (Lock) return _task; } }

    internal static void Ensure(ClipOperations operations)
    {
        lock (Lock)
        {
            if (_task is { IsCompleted: false }) return;
            _task = Task.Run(() => WorkAsync(operations));
        }
    }

    public static Task<bool> GetAsync(long clipId, CancellationToken cancellationToken) => GetAsync(clipId, new ClipOperations(), cancellationToken);

    internal static async Task<bool> GetAsync(long clipId, ClipOperations operations, CancellationToken cancellationToken = default)
    {
        await Queue.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, object?>? row;
            using (var con = Db.Open()) row = con.QueryOne("SELECT filepath, storage, duration, has_thumb, thumb_retry_at FROM clips WHERE id=$id", ("$id", clipId));
            if (row is null || row["storage"] as string == "cloud") return false;
            var thumbnail = Config.ThumbPath(clipId);
            bool exists = File.Exists(thumbnail);
            if (exists && row["duration"] is not null) return true;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (row["thumb_retry_at"] is not null && Convert.ToDouble(row["thumb_retry_at"]) > now) return exists;

            var path = (string)row["filepath"]!;
            double? duration = row["duration"] is null ? null : Convert.ToDouble(row["duration"]);
            bool ready = false;
            try
            {
                var before = new FileInfo(path);
                if (before.Exists)
                {
                    long length = before.Length;
                    var modified = before.LastWriteTimeUtc;
                    duration ??= operations.ProbeDuration(path);
                    ready = exists || operations.MakeThumbnail(clipId, path, duration ?? 10.0);
                    var after = new FileInfo(path);
                    if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != modified)
                    {
                        if (File.Exists(thumbnail)) File.Delete(thumbnail);
                        return false;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ready = false;
            }

            using (var con = Db.Open())
                con.Exec("UPDATE clips SET has_thumb=$h, duration=COALESCE($d,duration), thumb_retry_at=$retry WHERE id=$id AND filepath=$path",
                    ("$h", ready ? 1 : 2), ("$d", duration), ("$retry", ready && duration is not null ? null : now + 300), ("$id", clipId), ("$path", path));
            return ready;
        }
        finally { Queue.Release(); }
    }

    private static async Task WorkAsync(ClipOperations operations)
    {
        long lastId = 0;
        while (true)
        {
            using (var con = Db.Open())
            {
                var row = con.QueryOne(
                    "SELECT id FROM clips WHERE id > $after AND storage!='cloud' AND (has_thumb!=1 OR duration IS NULL) AND (thumb_retry_at IS NULL OR thumb_retry_at <= $now) ORDER BY id LIMIT 1",
                    ("$after", lastId), ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
                if (row is null) return;
                lastId = Convert.ToInt64(row["id"]);
            }
            await GetAsync(lastId, operations);
        }
    }
}
