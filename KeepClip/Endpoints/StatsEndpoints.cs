namespace KeepClip.Endpoints;

/// <summary>Library-wide statistics for the dashboard cards (<c>/api/stats</c>).</summary>
public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stats", (StatsRepository stats) =>
        {
            // Disk space isn't a DB concern — measure it here and let the repo do the rest.
            long? diskTotal = null, diskFree = null;
            try
            {
                var root = Settings.GetClipsRoot();
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
                    diskTotal = di.TotalSize;
                    diskFree = di.AvailableFreeSpace;
                }
            }
            catch { /* disk info best-effort */ }

            return Results.Json(stats.Get(diskTotal, diskFree));
        });
    }
}
