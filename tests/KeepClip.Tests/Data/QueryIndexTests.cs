namespace KeepClip.Tests.Data;

public class QueryIndexTests
{
    public QueryIndexTests() => TestDb.Reset();

    [Theory]
    [InlineData("SELECT id FROM clips WHERE remote_id='test' LIMIT 1", "idx_clips_remote")]
    [InlineData("SELECT id FROM clips ORDER BY mtime DESC LIMIT 120", "idx_clips_mtime")]
    [InlineData("SELECT id FROM clips WHERE game='Game' ORDER BY mtime DESC LIMIT 120", "idx_clips_game_mtime")]
    [InlineData("SELECT id FROM clips WHERE favorite=1 ORDER BY mtime DESC LIMIT 500", "idx_clips_favorite_mtime")]
    [InlineData("SELECT id FROM clips ORDER BY size_bytes DESC LIMIT 120", "idx_clips_size")]
    [InlineData("SELECT id FROM clips WHERE id>0 AND storage!='cloud' AND (has_thumb!=1 OR duration IS NULL) AND (thumb_retry_at IS NULL OR thumb_retry_at<=1700000000) ORDER BY id LIMIT 1", "idx_clips_thumbnail_pending")]
    [InlineData("SELECT id,start_s,end_s,text FROM segments WHERE clip_id=1 ORDER BY start_s", "idx_segments_clip_start")]
    public void Frequent_queries_use_an_index_without_a_temporary_sort(string query, string index)
    {
        using var con = Db.Open();
        var plan = string.Join("\n", con.Query("EXPLAIN QUERY PLAN " + query).Select(row => row["detail"]));
        Assert.Contains(index, plan);
        Assert.DoesNotContain("TEMP B-TREE", plan);
    }

    [Fact]
    public void Initialization_upgrades_existing_rows_and_preserves_duplicate_remote_ids()
    {
        var first = TestDb.AddClip();
        var second = TestDb.AddClip();
        TestDb.Exec("UPDATE clips SET remote_id='legacy-duplicate', favorite=1");
        TestDb.Exec("DROP INDEX idx_clips_remote; ALTER TABLE clips DROP COLUMN thumb_retry_at");
        Db.InitDb();
        Db.InitDb();
        Assert.Equal(2, TestDb.ScalarLong("SELECT COUNT(*) FROM clips WHERE remote_id='legacy-duplicate' AND favorite=1"));
        Assert.NotNull(new ClipRepository().GetById(first));
        Assert.NotNull(new ClipRepository().GetById(second));
        Assert.Equal(2, TestDb.ScalarLong("SELECT COUNT(*) FROM clips WHERE thumb_retry_at IS NULL"));
    }
}