using System.Net;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class ReplayGamesOnlyApiTests
{
    private readonly HttpClient _http;

    public ReplayGamesOnlyApiTests(ApiFactory factory)
    {
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    private sealed class CaptureScope : IDisposable
    {
        private readonly ReplayRecordingController _previous = ReplayService.Recording;
        internal readonly ReplayCaptureStub Backend = new();
        internal string? Game;

        internal CaptureScope() => ReplayService.Recording = new(Backend, () => Game);

        public void Dispose()
        {
            ReplayService.Shutdown();
            Settings.SetString("replay_enabled", "0");
            ReplayService.Recording = _previous;
            ReplayService.ApplyConfig();
        }
    }

    [Fact]
    public async Task Cleanup_failure_is_returned_in_status_without_starting_capture_or_failing_the_configuration_request()
    {
        using var capture = new CaptureScope();
        capture.Backend.CleanupError = new IOException("synthetic locked recording");
        var response = await _http.PostJson("/api/replay/config", new { enabled = true, games_only = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("synthetic locked recording", (string)(await response.Json())["error"]!);
        Assert.Equal(0, capture.Backend.Attempts);
        Assert.False((bool)(await _http.GetJson("/api/replay/status"))["can_save"]!);
    }

    [Fact]
    public async Task Games_only_can_be_enabled_on_the_desktop_without_starting_any_native_capture()
    {
        using var capture = new CaptureScope();
        var response = await _http.PostJson("/api/replay/config", new { enabled = true, games_only = true, mic = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Json();
        Assert.True((bool)state["games_only"]!);
        Assert.True((bool)state["waiting_for_game"]!);
        Assert.False((bool)state["running"]!);
        Assert.False((bool)state["can_save"]!);
        Assert.Equal("1", Settings.GetString("replay_games_only"));
        Assert.Equal(0, capture.Backend.Attempts);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.PostEmpty("/api/replay/save")).StatusCode);
        Assert.Equal(0, capture.Backend.Attempts);
    }

    [Fact]
    public async Task Status_distinguishes_recording_from_paused_with_a_saveable_game_buffer()
    {
        using var capture = new CaptureScope();
        await _http.PostJson("/api/replay/config", new { enabled = true, games_only = true });
        capture.Game = "Counter-Strike 2";
        ReplayService.Poll();
        var recording = await _http.GetJson("/api/replay/status");
        Assert.True((bool)recording["running"]!);
        Assert.False((bool)recording["waiting_for_game"]!);
        Assert.False((bool)recording["can_save"]!);
        capture.Backend.Frames.Add("synthetic game frame");
        capture.Game = null;
        ReplayService.Poll();

        var paused = await _http.GetJson("/api/replay/status");
        Assert.False((bool)paused["running"]!);
        Assert.True((bool)paused["waiting_for_game"]!);
        Assert.True((bool)paused["can_save"]!);
        Assert.Equal("Counter-Strike 2", (string)paused["game"]!);
        Assert.Single(capture.Backend.Starts);
    }

    [Fact]
    public async Task Omitted_games_only_preserves_the_setting_and_an_explicit_false_turns_it_off()
    {
        using var capture = new CaptureScope();
        await _http.PostJson("/api/replay/config", new { enabled = false, games_only = true });
        var unchanged = await _http.PostJson("/api/replay/config", new { enabled = false, duration_s = 30 });
        Assert.True((bool)(await unchanged.Json())["games_only"]!);
        var disabled = await _http.PostJson("/api/replay/config", new { enabled = false, games_only = false });
        Assert.False((bool)(await disabled.Json())["games_only"]!);
        Assert.False((bool)(await disabled.Json())["waiting_for_game"]!);
        Assert.Equal(0, capture.Backend.Attempts);
    }
}