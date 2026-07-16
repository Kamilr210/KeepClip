namespace KeepClip.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stats", (StatsRepository stats) =>
        {
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
            catch { /* Brak informacji o dysku nie blokuje statystyk. */ }

            return Results.Json(stats.Get(diskTotal, diskFree));
        });
    }
}
