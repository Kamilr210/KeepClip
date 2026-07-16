namespace KeepClip.Data;

public class StatsRepository
{
    public Dictionary<string, object?> Get(long? diskTotal, long? diskFree)
    {
        using var con = Db.Open();
        return new Dictionary<string, object?>
        {
            ["clips"] = con.ScalarLong("SELECT COUNT(*) FROM clips"),
            ["transcribed"] = con.ScalarLong("SELECT COUNT(*) FROM clips WHERE transcribed_at IS NOT NULL"),
            ["segments"] = con.ScalarLong("SELECT COUNT(*) FROM segments"),
            ["total_bytes"] = con.ScalarLong("SELECT COALESCE(SUM(size_bytes), 0) FROM clips"),
            ["local_bytes"] = con.ScalarLong(
                "SELECT COALESCE(SUM(size_bytes), 0) FROM clips WHERE storage IS NULL OR storage != 'cloud'"),
            ["cloud_bytes"] = con.ScalarLong("SELECT COALESCE(SUM(size_bytes), 0) FROM clips WHERE storage = 'cloud'"),
            ["disk_total"] = diskTotal,
            ["disk_free"] = diskFree,
            ["favorites"] = con.ScalarLong("SELECT COUNT(*) FROM clips WHERE favorite = 1"),
            ["games"] = con.Query(
                @"SELECT game, COUNT(*) AS clips,
                         SUM(CASE WHEN transcribed_at IS NOT NULL THEN 1 ELSE 0 END) AS done
                  FROM clips GROUP BY game ORDER BY game"),
        };
    }
}
