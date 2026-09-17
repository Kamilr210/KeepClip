using Microsoft.Data.Sqlite;

namespace KeepClip.Tests.Data;

public class ClipRepositoryTests
{
    private readonly ClipRepository _clips = new();

    public ClipRepositoryTests() => TestDb.Reset();

    [Fact]
    public void Insert_then_GetById_maps_every_field()
    {
        var id = _clips.Insert("CS2", "a.mp4", @"C:\klipy\CS2\a.mp4", 1234, 1_700_000_000.5);

        var clip = _clips.GetById(id);

        Assert.NotNull(clip);
        Assert.Equal(id, clip.Id);
        Assert.Equal("CS2", clip.Game);
        Assert.Equal("a.mp4", clip.Filename);
        Assert.Equal(@"C:\klipy\CS2\a.mp4", clip.Filepath);
        Assert.Equal(1234, clip.SizeBytes);
        Assert.Equal(1_700_000_000.5, clip.Mtime);
        Assert.Equal(0, clip.Favorite);
        Assert.Equal("local", clip.Storage);
        Assert.Null(clip.TranscribedAt);
        Assert.Null(clip.RemoteId);
    }

    [Fact]
    public void GetById_and_GetRow_return_null_for_unknown_clip()
    {
        Assert.Null(_clips.GetById(12345));
        Assert.Null(_clips.GetRow(12345));
    }

    [Fact]
    public void Insert_rejects_duplicate_path()
    {
        _clips.Insert("Gra", "a.mp4", @"C:\klipy\a.mp4", 1, 1);

        Assert.Throws<SqliteException>(() => _clips.Insert("Inna", "a.mp4", @"C:\klipy\a.mp4", 1, 1));
    }

    [Fact]
    public void List_sorts_newest_first_by_default()
    {
        var old = TestDb.AddClip(mtime: 100);
        var newest = TestDb.AddClip(mtime: 300);
        var middle = TestDb.AddClip(mtime: 200);

        var ids = _clips.List(null, null, 10, false).Select(r => Convert.ToInt64(r["id"])).ToList();

        Assert.Equal(new[] { newest, middle, old }, ids);
    }

    [Theory]
    [InlineData("oldest", "a,b,c")]
    [InlineData("newest", "c,b,a")]
    [InlineData("largest", "b,c,a")]
    [InlineData("smallest", "a,c,b")]
    [InlineData("nieznane", "c,b,a")]
    public void List_supports_every_sort(string sort, string expectedOrder)
    {
        var a = TestDb.AddClip(name: "a.mp4", size: 10, mtime: 1);
        var b = TestDb.AddClip(name: "b.mp4", size: 30, mtime: 2);
        var c = TestDb.AddClip(name: "c.mp4", size: 20, mtime: 3);
        var byName = new Dictionary<long, string> { [a] = "a", [b] = "b", [c] = "c" };

        var order = _clips.List(null, sort, 10, false).Select(r => byName[Convert.ToInt64(r["id"])]);

        Assert.Equal(expectedOrder, string.Join(",", order));
    }

    [Fact]
    public void List_filters_by_game_and_respects_limit()
    {
        TestDb.AddClip("CS2");
        TestDb.AddClip("CS2");
        TestDb.AddClip("Factorio");

        Assert.Equal(2, _clips.List("CS2", null, 10, false).Count);
        Assert.Single(_clips.List("Factorio", null, 10, false));
        Assert.Empty(_clips.List("Brak", null, 10, false));
        Assert.Equal(2, _clips.List(null, null, 2, false).Count);
    }

    [Fact]
    public void ToggleFavorite_flips_state_and_List_can_show_only_favorites()
    {
        var id = TestDb.AddClip();
        TestDb.AddClip();

        Assert.True(_clips.ToggleFavorite(id));
        Assert.Equal(1, _clips.GetById(id)!.Favorite);
        var favorites = _clips.List(null, null, 10, favoritesOnly: true);
        Assert.Single(favorites);
        Assert.Equal(id, Convert.ToInt64(favorites[0]["id"]));

        Assert.False(_clips.ToggleFavorite(id));
        Assert.Empty(_clips.List(null, null, 10, favoritesOnly: true));
    }

    [Fact]
    public void ToggleFavorite_returns_null_for_unknown_clip()
        => Assert.Null(_clips.ToggleFavorite(999));

    [Fact]
    public void Count_reflects_inserts_and_deletes()
    {
        Assert.Equal(0, _clips.Count());
        var id = TestDb.AddClip();
        TestDb.AddClip();
        Assert.Equal(2, _clips.Count());

        _clips.Delete(id);

        Assert.Equal(1, _clips.Count());
    }

    [Fact]
    public void SetTranscribed_stores_timestamp_and_language()
    {
        var id = TestDb.AddClip();

        _clips.SetTranscribed(id, "2026-09-17T10:00:00+00:00", "pl");

        Assert.Equal("2026-09-17T10:00:00+00:00", _clips.GetById(id)!.TranscribedAt);
        Assert.Equal("pl", _clips.GetRow(id)!["language"]);
    }

    [Fact]
    public void UpdateAfterFix_rewrites_size_mtime_duration_and_thumb_state()
    {
        var id = TestDb.AddClip(size: 10, mtime: 1);

        _clips.UpdateAfterFix(id, 20, 2.5, 33.3, 1);

        var clip = _clips.GetById(id)!;
        Assert.Equal(20, clip.SizeBytes);
        Assert.Equal(2.5, clip.Mtime);
        Assert.Equal(33.3, clip.Duration);
        Assert.Equal(1, clip.HasThumb);
    }

    [Fact]
    public void SetThumbState_updates_only_that_column()
    {
        var id = TestDb.AddClip(size: 77);

        _clips.SetThumbState(id, 2);

        var clip = _clips.GetById(id)!;
        Assert.Equal(2, clip.HasThumb);
        Assert.Equal(77, clip.SizeBytes);
    }

    [Fact]
    public void Delete_cascades_to_segments_and_folder_links()
    {
        var id = TestDb.AddClip();
        new SegmentRepository().Insert(id, 0, 1, "tekst");
        var folder = new FolderRepository().Create("F")!;
        new FolderRepository().AddClip(Convert.ToInt64(folder["id"]), id);

        _clips.Delete(id);

        Assert.Null(_clips.GetById(id));
        Assert.Empty(new SegmentRepository().ListByClipId(id));
        Assert.Equal(0, TestDb.ScalarLong("SELECT COUNT(*) FROM folder_clips"));
    }
}
