using System.Net;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class BackgroundApiTests
{
    private readonly HttpClient _http;
    public BackgroundApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task Starting_an_empty_transcription_job_returns_its_count_and_finishes_the_event_stream()
    {
        var response = await _http.PostEmpty("/api/transcribe/start?force=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Json();
        Assert.True((bool)status["started"]!);
        Assert.Equal(0, (int)status["total"]!);
        await TranscribeWorker.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await _http.GetAsync("/api/transcribe/stream", cts.Token);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", stream.Headers.CacheControl?.ToString());
        Assert.StartsWith("data: ", await stream.Content.ReadAsStringAsync(cts.Token));
    }

    [Theory]
    [InlineData("duration_s", 14, "replayBadDuration")]
    [InlineData("duration_s", 601, "replayBadDuration")]
    [InlineData("fps", 24, "replayBadFps")]
    public async Task Invalid_replay_numbers_do_not_change_settings(string key, int value, string error)
    {
        var response = await _http.PostJson("/api/replay/config", new Dictionary<string, object> { [key] = value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, (string)(await response.Json())["error"]!);
        Assert.Null(Settings.GetString("replay_" + key));
    }

    [Theory]
    [InlineData("quality", "ultra", "replayBadQuality")]
    [InlineData("hotkey", "F10", "replayBadHotkey")]
    public async Task Invalid_replay_choices_do_not_change_settings(string key, string value, string error)
    {
        var response = await _http.PostJson("/api/replay/config", new Dictionary<string, object> { [key] = value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, (string)(await response.Json())["error"]!);
        Assert.Null(Settings.GetString("replay_" + key));
    }

    [Fact]
    public async Task Disabled_replay_configuration_can_be_saved_without_starting_capture()
    {
        var response = await _http.PostJson("/api/replay/config", new { enabled = false, background = false, duration_s = 15, fps = 30, quality = "low", hotkey = "Alt+F9", mic = false, audio_output = "test-output", audio_input = "test-input" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Json();
        Assert.False((bool)state["running"]!);
        Assert.Equal(15, (int)state["duration_s"]!);
        Assert.Equal(30, (int)state["fps"]!);
        Assert.Equal("test-output", (string)state["audio_output"]!);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostEmpty("/api/replay/save")).StatusCode);
    }
}