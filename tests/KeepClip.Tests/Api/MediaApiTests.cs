using System.Net;
using System.Net.Http.Headers;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class MediaApiTests
{
    private readonly HttpClient _http;

    public MediaApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Theory]
    [InlineData("mp4", "video/mp4")]
    [InlineData("WEBM", "video/webm")]
    [InlineData("mov", "video/quicktime")]
    [InlineData("mkv", "video/x-matroska")]
    [InlineData("avi", "video/x-msvideo")]
    [InlineData("unknown", "video/mp4")]
    public async Task Video_returns_only_the_registered_file_with_its_content_type(string extension, string contentType)
    {
        var directory = TestEnvironment.NewTempDir("video");
        var content = new byte[] { 0, 1, 2, 3, 4, 5 };
        File.WriteAllBytes(Path.Combine(directory, "clip." + extension), content);
        var clip = TestDb.AddClip(name: "clip." + extension, dir: directory);

        var response = await _http.GetAsync($"/video/{clip}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Video_honors_byte_ranges_without_sending_the_whole_file()
    {
        var directory = TestEnvironment.NewTempDir("video-range");
        File.WriteAllBytes(Path.Combine(directory, "clip.mp4"), new byte[] { 10, 20, 30, 40, 50 });
        var clip = TestDb.AddClip(name: "clip.mp4", dir: directory);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/video/{clip}");
        request.Headers.Range = new RangeHeaderValue(1, 3);

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 1-3/5", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal(new byte[] { 20, 30, 40 }, await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/video/999")]
    [InlineData("/playback/999")]
    [InlineData("/api/playback/999?start=false")]
    public async Task Unknown_clip_returns_not_found(string url)
        => Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(url)).StatusCode);

    [Fact]
    public async Task Missing_local_file_is_not_served_and_is_reported_by_playback_status()
    {
        var clip = TestDb.AddClip();

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/video/{clip}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/playback/{clip}")).StatusCode);
        var status = await _http.GetJson($"/api/playback/{clip}?start=false");
        Assert.False((bool)status["ready"]!);
        Assert.False((bool)status["preparing"]!);
        Assert.Equal("error", (string)status["stage"]!);
    }

    [Fact]
    public async Task Cloud_clip_without_a_remote_id_is_rejected_without_connecting_to_google()
    {
        var clip = TestDb.AddClip();
        TestDb.Exec("UPDATE clips SET storage='cloud' WHERE id=$id", ("$id", clip));

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/video/{clip}")).StatusCode);
    }

    [Fact]
    public async Task Cloud_playback_status_uses_the_video_route_without_starting_a_local_encoder()
    {
        var clip = TestDb.AddClip();
        TestDb.Exec("UPDATE clips SET storage='cloud', remote_id='test-id' WHERE id=$id", ("$id", clip));

        var status = await _http.GetJson($"/api/playback/{clip}?start=false");

        Assert.True((bool)status["ready"]!);
        Assert.False((bool)status["preparing"]!);
        Assert.False((bool)status["proxy"]!);
        Assert.StartsWith($"/video/{clip}?", (string)status["url"]!);
    }

    [Fact]
    public async Task Explorer_rejects_missing_paths_without_launching_a_process()
    {
        var path = Path.Combine(TestEnvironment.NewTempDir("missing-path"), "missing.mp4");

        var response = await _http.PostJson("/api/show-in-explorer", new { path });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}