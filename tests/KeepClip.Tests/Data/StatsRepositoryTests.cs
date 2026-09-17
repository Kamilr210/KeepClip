namespace KeepClip.Tests.Data;

public class StatsRepositoryTests
{
    private readonly StatsRepository _stats = new();

    public StatsRepositoryTests() => TestDb.Reset();

    [Fact]
    public void Empty_library_reports_zeros_and_passes_disk_numbers_through()
    {
        var s = _stats.Get(1000, 400);

        Assert.Equal(0L, s["clips"]);
        Assert.Equal(0L, s["transcribed"]);
        Assert.Equal(0L, s["segments"]);
        Assert.Equal(0L, s["total_bytes"]);
        Assert.Equal(0L, s["favorites"]);
        Assert.Equal(1000L, s["disk_total"]);
        Assert.Equal(400L, s["disk_free"]);
        Assert.Empty((List<Dictionary<string, object?>>)s["games"]!);
    }

    [Fact]
    public void Counts_split_local_and_cloud_bytes_and_group_by_game()
    {
        var clips = new ClipRepository();
        var a = TestDb.AddClip("CS2", size: 100);
        var b = TestDb.AddClip("CS2", size: 200);
        var c = TestDb.AddClip("Factorio", size: 50);
        clips.SetTranscribed(a, "2026-01-01T00:00:00+00:00", "pl");
        clips.ToggleFavorite(b);
        new SegmentRepository().Insert(a, 0, 1, "x");
        new SegmentRepository().Insert(a, 1, 2, "y");
        TestDb.Exec("UPDATE clips SET storage='cloud' WHERE id=$id", ("$id", c));

        var s = _stats.Get(null, null);

        Assert.Equal(3L, s["clips"]);
        Assert.Equal(1L, s["transcribed"]);
        Assert.Equal(2L, s["segments"]);
        Assert.Equal(350L, s["total_bytes"]);
        Assert.Equal(300L, s["local_bytes"]);
        Assert.Equal(50L, s["cloud_bytes"]);
        Assert.Equal(1L, s["favorites"]);
        Assert.Null(s["disk_total"]);

        var games = (List<Dictionary<string, object?>>)s["games"]!;
        Assert.Equal(new object?[] { "CS2", "Factorio" }, games.Select(g => g["game"]).ToList());
        Assert.Equal(2L, games[0]["clips"]);
        Assert.Equal(1L, games[0]["done"]);
        Assert.Equal(0L, games[1]["done"]);
    }
}
