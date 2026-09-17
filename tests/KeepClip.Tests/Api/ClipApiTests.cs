using System.Net;
using System.Text;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class ClipApiTests
{
    private readonly HttpClient _http;

    public ClipApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task Clip_list_supports_game_sort_limit_and_favorites()
    {
        var a = TestDb.AddClip("CS2", "a.mp4", size: 10, mtime: 1);
        var b = TestDb.AddClip("CS2", "b.mp4", size: 30, mtime: 2);
        TestDb.AddClip("Factorio", "c.mp4", size: 20, mtime: 3);

        Assert.Equal(3, (await _http.GetJson("/api/clips")).AsArray().Count);
        Assert.Equal(2, (await _http.GetJson("/api/clips?game=CS2")).AsArray().Count);
        Assert.Single((await _http.GetJson("/api/clips?limit=1")).AsArray());
        Assert.Equal(b, (long)(await _http.GetJson("/api/clips?sort=largest")).AsArray()[0]!["id"]!);
        Assert.Empty((await _http.GetJson("/api/clips?favorite=1")).AsArray());

        var toggle = await _http.PostEmpty($"/api/clips/{a}/favorite");
        Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);
        Assert.True((bool)(await toggle.Json())["favorite"]!);
        Assert.Single((await _http.GetJson("/api/clips?favorite=1")).AsArray());
    }

    [Fact]
    public async Task Favorite_toggle_on_missing_clip_is_404()
    {
        var response = await _http.PostEmpty("/api/clips/999/favorite");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Segments_are_returned_with_their_clip_and_can_be_edited()
    {
        var clip = TestDb.AddClip();
        new SegmentRepository().Insert(clip, 0, 1, "pierwotny");

        var json = await _http.GetJson($"/api/segments/{clip}");
        Assert.Equal(clip, (long)json["clip"]!["id"]!);
        var segment = Assert.Single(json["segments"]!.AsArray());
        var segmentId = (long)segment!["id"]!;

        var empty = await _http.PatchJson($"/api/segments/{segmentId}", new { text = " " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal("segmentTextEmpty", (string)(await empty.Json())["error"]!);

        var missing = await _http.PatchJson("/api/segments/999", new { text = "x" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var ok = await _http.PatchJson($"/api/segments/{segmentId}", new { text = " poprawiony " });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("poprawiony", (string)(await ok.Json())["text"]!);
        var after = await _http.GetJson($"/api/segments/{clip}");
        Assert.Equal("poprawiony", (string)after["segments"]![0]!["text"]!);
    }

    [Fact]
    public async Task Segments_of_missing_clip_are_404()
    {
        Settings.SetLanguage("pl");
        var response = await _http.GetAsync("/api/segments/999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Klip nie istnieje.", (string)(await response.Json())["detail"]!);
    }

    [Fact]
    public async Task Error_messages_follow_the_saved_language()
    {
        Settings.SetLanguage("en");
        var response = await _http.GetAsync("/api/segments/999");
        Assert.Equal("The clip does not exist.", (string)(await response.Json())["detail"]!);
    }

    [Fact]
    public async Task Search_validates_query_and_finds_spoken_words()
    {
        var clip = TestDb.AddClip("CS2", "mecz.mp4");
        new SegmentRepository().Insert(clip, 12, 14, "to był niesamowity headshot");

        Assert.Equal(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/search")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.GetAsync("/api/search?q=")).StatusCode);

        var symbols = await _http.GetJson("/api/search?q=%3F%3F%3F");
        Assert.Empty(symbols["results"]!.AsArray());

        var hit = await _http.GetJson("/api/search?q=niesamowity%20head");
        Assert.Equal("\"niesamowity\"* AND \"head\"*", (string)hit["fts"]!);
        var result = Assert.Single(hit["results"]!.AsArray());
        Assert.Equal(clip, (long)result!["clip_id"]!);
        Assert.Equal(12, (double)result["start_s"]!);
        Assert.Contains("<mark>", (string)result["snippet"]!);

        Assert.Empty((await _http.GetJson("/api/search?q=nieobecne"))["results"]!.AsArray());
    }

    [Fact]
    public async Task Stats_summarize_the_library()
    {
        var clip = TestDb.AddClip("CS2", size: 100);
        new ClipRepository().ToggleFavorite(clip);
        Settings.SetClipsRoot(TestEnvironment.NewTempDir("stats"));

        var json = await _http.GetJson("/api/stats");

        Assert.Equal(1, (int)json["clips"]!);
        Assert.Equal(1, (int)json["favorites"]!);
        Assert.Equal(100, (long)json["total_bytes"]!);
        Assert.True((long)json["disk_total"]! > 0);
        Assert.Equal("CS2", (string)json["games"]![0]!["game"]!);
    }

    [Fact]
    public async Task Transcript_download_needs_segments_and_returns_a_text_file()
    {
        var clip = TestDb.AddClip("CS2", "Counter-strike 2 2026.09.15.mp4", mtime: 1_757_970_000);

        var empty = await _http.GetAsync($"/api/clips/{clip}/transcript.txt");
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        Assert.Equal("transcriptEmpty", (string)(await empty.Json())["error"]!);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/clips/999/transcript.txt")).StatusCode);

        new SegmentRepository().Insert(clip, 3661.2, 3663, "godzina później");
        new SegmentRepository().Insert(clip, 1.5, 3, "na początku");

        var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/clips/{clip}/transcript.txt"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);

        var response = await _http.GetAsync($"/api/clips/{clip}/transcript.txt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("Counter-strike 2 2026.09.15.txt", response.Content.Headers.ContentDisposition?.ToString());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("Counter-strike 2 2026.09.15.mp4", text.TrimStart('﻿'));
        Assert.Contains("[00:01] na początku", text);
        Assert.Contains("[1:01:01] godzina później", text);
        Assert.True(text.IndexOf("[00:01]", StringComparison.Ordinal) < text.IndexOf("[1:01:01]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_a_clip_removes_it_from_the_library()
    {
        var clip = TestDb.AddClip();
        new SegmentRepository().Insert(clip, 0, 1, "x");

        var response = await _http.DeleteAsync($"/api/clips/{clip}?delete_file=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Json();
        Assert.Equal(clip, (long)json["deleted_clip_id"]!);
        Assert.False((bool)json["file_sent_to_trash"]!);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/segments/{clip}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/clips/{clip}")).StatusCode);
    }

    [Fact]
    public async Task Operations_on_missing_clips_fail_cleanly_without_tools()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostEmpty("/api/clips/999/retranscribe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostEmpty("/api/clips/999/fix")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/thumb/999")).StatusCode);
        Assert.Empty((await _http.GetJson("/api/clips/999/folders"))["folder_ids"]!.AsArray());
    }
}
