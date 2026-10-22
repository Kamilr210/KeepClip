using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class ClipOperationApiTests : IDisposable
{
    private readonly FakeClipOperations _operations = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;
    private readonly string _root = TestEnvironment.NewTempDir("clip-api-operations");
    private readonly long _id;

    public ClipOperationApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        Settings.SetClipsRoot(_root);
        File.WriteAllText(Path.Combine(_root, "clip.mp4"), "original");
        _id = TestDb.AddClip(name: "clip.mp4", dir: _root);
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ClipService>();
            services.AddSingleton(new ClipService(new ClipRepository(), new SegmentRepository(), new PlaybackProxyService(_operations), _operations));
        }));
        _http = _factory.CreateClient();
    }

    public void Dispose() { _http.Dispose(); _factory.Dispose(); }

    [Fact]
    public async Task Cut_returns_the_new_clip_and_completes_progress()
    {
        _operations.Cut = (_, output, start, end, size, progress) =>
        {
            Assert.Equal(1, start);
            Assert.Equal(4, end);
            Assert.Equal(25, size);
            File.WriteAllText(output, "cut");
            progress!(0.5, "cutting");
            return (true, "ok", new() { ["duration_s"] = 3.0 });
        };
        var response = await _http.PostJson($"/api/clips/{_id}/cut", new { start = 1, end = 4, target_size_mb = 25 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Json();
        Assert.True((bool)json["in_library"]!);
        Assert.NotEqual(_id, (long)json["clip_id"]!);
        var progress = await _http.GetJson("/api/single-operation/status");
        Assert.Equal("cut", (string)progress["kind"]!);
        Assert.False((bool)progress["active"]!);
        Assert.True((bool)progress["success"]!);
        Assert.Equal(1, (double)progress["progress"]!);
    }

    [Theory]
    [InlineData(-1.0, 2.0, -1.0)]
    [InlineData(0.0, 0.1, 25.0)]
    [InlineData(0.0, 1801.0, 25.0)]
    public async Task Invalid_cut_body_returns_422_and_marks_the_operation_failed(double start, double end, double targetSize)
    {
        var response = await _http.PostJson($"/api/clips/{_id}/cut", new { start, end, target_size_mb = targetSize });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var progress = await _http.GetJson("/api/single-operation/status");
        Assert.False((bool)progress["active"]!);
        Assert.False((bool)progress["success"]!);
        Assert.NotNull(progress["error"]);
        Assert.Equal(1, new ClipRepository().Count());
    }

    [Fact]
    public async Task Unexpected_encoder_exception_still_finishes_the_progress_state()
    {
        _operations.Cut = (_, _, _, _, _, _) => throw new IOException("synthetic encoder failure");
        var response = await _http.PostJson($"/api/clips/{_id}/cut", new { start = 0, end = 5 });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var state = await _http.GetJson("/api/single-operation/status");
        Assert.False((bool)state["active"]!);
        Assert.False((bool)state["success"]!);
        Assert.Equal("synthetic encoder failure", (string)state["error"]!);
    }

    [Fact]
    public async Task Retranscription_updates_segments_and_completes_progress()
    {
        _operations.Transcription = (_, progress) => { progress!(0.6, "transcribing"); return new() { new(0, 1, "new transcript") }; };
        var response = await _http.PostEmpty($"/api/clips/{_id}/retranscribe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (int)(await response.Json())["segments"]!);
        Assert.Equal("new transcript", Assert.Single(new SegmentRepository().ListByClipId(_id))["text"]);
        var state = await _http.GetJson("/api/single-operation/status");
        Assert.True((bool)state["success"]!);
        Assert.Equal("transcription", (string)state["kind"]!);
    }

    [Fact]
    public async Task Failed_recycling_returns_500_and_retains_the_clip()
    {
        _operations.SendToTrashWithRetry = _ => "synthetic sharing violation";
        var response = await _http.DeleteAsync($"/api/clips/{_id}?delete_file=true");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(new ClipRepository().GetById(_id));
        Assert.True(File.Exists(Path.Combine(_root, "clip.mp4")));
    }

    [Fact]
    public async Task Missing_cloud_configuration_cannot_start_login_or_return_an_avatar()
    {
        Assert.False(File.Exists(Config.GoogleClientPath));
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostEmpty("/api/cloud/connect")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/cloud/avatar")).StatusCode);
    }
}