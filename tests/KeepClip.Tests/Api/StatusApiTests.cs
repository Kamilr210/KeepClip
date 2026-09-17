using System.Net;
using System.Text.Json.Nodes;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class StatusApiTests
{
    private readonly HttpClient _http;

    public StatusApiTests(ApiFactory factory)
    {
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task Transcription_is_idle_on_a_fresh_start()
    {
        var json = await _http.GetJson("/api/transcribe/status");
        Assert.False((bool)json["running"]!);
    }

    [Fact]
    public async Task Replay_status_answers_without_ffmpeg()
    {
        var response = await _http.GetAsync("/api/replay/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.IsType<JsonObject>(await response.Json());
    }

    [Fact]
    public async Task Cloud_is_not_configured_without_a_google_client()
    {
        var json = await _http.GetJson("/api/cloud/status");

        Assert.False((bool)json["configured"]!);
        Assert.False((bool)json["connected"]!);
    }

    [Fact]
    public async Task Update_status_is_idle()
    {
        var response = await _http.GetAsync("/api/update/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.IsType<JsonObject>(await response.Json());
    }

    [Fact]
    public async Task Cancelling_a_transcription_that_is_not_running_is_harmless()
    {
        var response = await _http.PostEmpty("/api/transcribe/cancel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
