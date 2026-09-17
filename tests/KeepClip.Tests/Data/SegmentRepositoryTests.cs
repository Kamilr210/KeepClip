namespace KeepClip.Tests.Data;

public class SegmentRepositoryTests
{
    private readonly SegmentRepository _segments = new();

    public SegmentRepositoryTests() => TestDb.Reset();

    [Fact]
    public void ListByClipId_returns_segments_ordered_by_start()
    {
        var clip = TestDb.AddClip();
        _segments.Insert(clip, 5, 6, "trzeci");
        _segments.Insert(clip, 0, 1, "pierwszy");
        _segments.Insert(clip, 2, 3, "drugi");

        var texts = _segments.ListByClipId(clip).Select(r => r["text"]).ToList();

        Assert.Equal(new object?[] { "pierwszy", "drugi", "trzeci" }, texts);
    }

    [Fact]
    public void ListByClipId_is_scoped_to_one_clip()
    {
        var a = TestDb.AddClip();
        var b = TestDb.AddClip();
        _segments.Insert(a, 0, 1, "a");
        _segments.Insert(b, 0, 1, "b");

        Assert.Single(_segments.ListByClipId(a));
        Assert.Empty(_segments.ListByClipId(999));
    }

    [Fact]
    public void UpdateText_changes_text_and_search_index()
    {
        var clip = TestDb.AddClip();
        _segments.Insert(clip, 0, 1, "stary tekst");
        var id = Convert.ToInt64(_segments.ListByClipId(clip)[0]["id"]);

        Assert.True(_segments.UpdateText(id, "nowy tekst"));

        Assert.Equal("nowy tekst", _segments.ListByClipId(clip)[0]["text"]);
        Assert.Empty(_segments.Search("\"stary\"*", null, 10));
        Assert.Single(_segments.Search("\"nowy\"*", null, 10));
    }

    [Fact]
    public void UpdateText_returns_false_for_unknown_segment()
        => Assert.False(_segments.UpdateText(999, "x"));

    [Fact]
    public void ReplaceAll_swaps_the_whole_transcript()
    {
        var clip = TestDb.AddClip();
        _segments.Insert(clip, 0, 1, "stare");

        _segments.ReplaceAll(clip, new[] { (0.0, 1.0, "nowe A"), (1.0, 2.0, "nowe B") });

        var texts = _segments.ListByClipId(clip).Select(r => r["text"]).ToList();
        Assert.Equal(new object?[] { "nowe A", "nowe B" }, texts);
        Assert.Empty(_segments.Search("\"stare\"*", null, 10));
    }

    [Fact]
    public void DeleteByClipId_removes_only_that_clip()
    {
        var a = TestDb.AddClip();
        var b = TestDb.AddClip();
        _segments.Insert(a, 0, 1, "a");
        _segments.Insert(b, 0, 1, "b");

        _segments.DeleteByClipId(a);

        Assert.Empty(_segments.ListByClipId(a));
        Assert.Single(_segments.ListByClipId(b));
    }

    [Fact]
    public void ShiftTimestamps_moves_segments_and_drops_those_before_zero()
    {
        var clip = TestDb.AddClip();
        _segments.Insert(clip, 0, 2, "znika");
        _segments.Insert(clip, 3, 5, "zostaje");
        _segments.Insert(clip, 2, 4, "przycięty");

        var (shifted, dropped) = _segments.ShiftTimestamps(clip, 2.5);

        Assert.Equal(2, shifted);
        Assert.Equal(1, dropped);
        var rows = _segments.ListByClipId(clip);
        var cut = rows.Single(r => (string)r["text"]! == "przycięty");
        Assert.Equal(0.0, Convert.ToDouble(cut["start_s"]));
        Assert.Equal(1.5, Convert.ToDouble(cut["end_s"]));
        var kept = rows.Single(r => (string)r["text"]! == "zostaje");
        Assert.Equal(0.5, Convert.ToDouble(kept["start_s"]));
        Assert.Equal(2.5, Convert.ToDouble(kept["end_s"]));
    }

    [Fact]
    public void Search_ignores_diacritics_and_matches_prefixes()
    {
        var clip = TestDb.AddClip("CS2", "mecz.mp4");
        _segments.Insert(clip, 1, 2, "Gęślą jaźń świetnie");

        var byAscii = _segments.Search(SearchEndpoints.ToFtsQuery("gesla"), null, 10);
        var byPrefix = _segments.Search(SearchEndpoints.ToFtsQuery("jaź"), null, 10);
        var noMatch = _segments.Search(SearchEndpoints.ToFtsQuery("xylofon"), null, 10);

        var hit = Assert.Single(byAscii);
        Assert.Equal(clip, Convert.ToInt64(hit["clip_id"]));
        Assert.Equal("CS2", hit["game"]);
        Assert.Contains("<mark>", (string)hit["snippet"]!);
        Assert.Single(byPrefix);
        Assert.Empty(noMatch);
    }

    [Fact]
    public void Search_requires_every_word()
    {
        var clip = TestDb.AddClip();
        _segments.Insert(clip, 0, 1, "headshot przez smoke");
        _segments.Insert(clip, 1, 2, "headshot z awp");

        var both = _segments.Search(SearchEndpoints.ToFtsQuery("headshot smoke"), null, 10);
        var any = _segments.Search(SearchEndpoints.ToFtsQuery("headshot"), null, 10);

        Assert.Single(both);
        Assert.Equal(2, any.Count);
    }

    [Fact]
    public void Search_sorts_by_clip_date_when_asked()
    {
        var older = TestDb.AddClip(mtime: 100);
        var newer = TestDb.AddClip(mtime: 200);
        _segments.Insert(older, 0, 1, "słowo");
        _segments.Insert(newer, 0, 1, "słowo");

        var newestFirst = _segments.Search(SearchEndpoints.ToFtsQuery("słowo"), "newest", 10);
        var oldestFirst = _segments.Search(SearchEndpoints.ToFtsQuery("słowo"), "oldest", 10);

        Assert.Equal(newer, Convert.ToInt64(newestFirst[0]["clip_id"]));
        Assert.Equal(older, Convert.ToInt64(oldestFirst[0]["clip_id"]));
        Assert.Single(_segments.Search(SearchEndpoints.ToFtsQuery("słowo"), null, 1));
    }
}
