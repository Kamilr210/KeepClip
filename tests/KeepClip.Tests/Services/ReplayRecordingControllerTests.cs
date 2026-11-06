namespace KeepClip.Tests.Services;

public class ReplayRecordingControllerTests
{
    private sealed class DeniedCleanup : ReplayCaptureBackend
    {
        internal override bool Running => false;
        internal override bool HasBuffer => true;
        internal override ReplayStartResult Start(bool freshRing, Func<bool> stillAllowed) => throw new InvalidOperationException("Cannot start before successful cleanup");
        internal override void Stop() { }
        internal override void Clear() => throw new UnauthorizedAccessException("synthetic denied cleanup");
    }

    private sealed class Clock : TimeProvider
    {
        internal long Seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }

    private readonly ReplayCaptureStub _capture = new();
    private readonly Clock _clock = new();
    private readonly ReplayRecordingController _controller;
    private string? _game;

    public ReplayRecordingControllerTests()
        => _controller = new(_capture, () => _game, _clock);

    private void StartGame(string game = "Counter-Strike 2")
    {
        _game = game;
        _controller.Configure(true, true, "configuration");
        _capture.Frames.Add(game + " frame");
    }

    [Fact]
    public void Denied_cleanup_does_not_escape_startup_or_shutdown_and_never_starts_capture()
    {
        var controller = new ReplayRecordingController(new DeniedCleanup(), () => "Counter-Strike 2");
        controller.Configure(false, true, "configuration");
        Assert.Equal("synthetic denied cleanup", controller.Error);
        controller.Configure(true, true, "configuration");
        controller.Tick();
        Assert.False(controller.CanSave);
        controller.Shutdown();
        Assert.Equal("synthetic denied cleanup", controller.Error);
    }

    [Fact]
    public void Enabling_on_the_desktop_waits_without_even_attempting_to_start_capture()
    {
        _controller.Configure(true, true, "configuration");
        for (int i = 0; i < 10; i++) _controller.Tick();

        Assert.True(_controller.WaitingForGame);
        Assert.False(_controller.Running);
        Assert.False(_controller.CanSave);
        Assert.Equal(0, _capture.Attempts);
    }

    [Fact]
    public void Disabled_recording_does_not_start_even_when_a_game_is_active()
    {
        _game = "Counter-Strike 2";
        _controller.Configure(false, true, "configuration");
        _controller.Tick();

        Assert.False(_controller.WaitingForGame);
        Assert.False(_controller.Running);
        Assert.Equal(0, _capture.Attempts);
    }

    [Fact]
    public void Normal_mode_still_records_the_desktop_and_reapplying_identical_settings_does_not_restart_it()
    {
        _controller.Configure(true, false, "configuration");
        _capture.Frames.Add("desktop frame");
        _controller.Configure(true, false, "configuration");
        for (int i = 0; i < 5; i++) _controller.Tick();

        Assert.True(_controller.Running);
        Assert.False(_controller.WaitingForGame);
        Assert.Equal(new[] { true }, _capture.Starts);
        Assert.Equal(new[] { "desktop frame" }, _capture.Frames);
    }

    [Fact]
    public void Leaving_a_game_pauses_capture_but_preserves_a_saveable_buffer_and_its_name()
    {
        StartGame();
        _game = null;
        _controller.Tick();
        _clock.Seconds = 3600;
        _controller.Tick();

        Assert.False(_controller.Running);
        Assert.True(_controller.WaitingForGame);
        Assert.True(_controller.CanSave);
        Assert.True(_controller.TryBeginSave(out var game));
        Assert.Equal("Counter-Strike 2", game);
        Assert.False(_controller.TryBeginSave(out _));
        _controller.EndSave();
        Assert.False(_controller.Running);
        Assert.Single(_capture.Frames);
    }

    [Fact]
    public void Returning_to_the_same_game_appends_to_the_preserved_buffer()
    {
        StartGame();
        _game = null;
        _controller.Tick();
        _game = "Counter-Strike 2";
        _controller.Tick();

        Assert.True(_controller.Running);
        Assert.Equal(new[] { true, false }, _capture.Starts);
        Assert.Equal(new[] { "Counter-Strike 2 frame" }, _capture.Frames);
    }

    [Fact]
    public void Changing_games_starts_a_clean_buffer_instead_of_mixing_two_games()
    {
        StartGame();
        _game = "Fortnite";
        _controller.Tick();

        Assert.True(_controller.Running);
        Assert.Equal("Fortnite", _controller.BufferGame);
        Assert.Equal(new[] { true, true }, _capture.Starts);
        Assert.Empty(_capture.Frames);
        Assert.False(_controller.CanSave);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changing_game_or_configuration_during_save_preserves_the_input_until_the_save_finishes(bool changeConfiguration)
    {
        StartGame();
        Assert.True(_controller.TryBeginSave(out var savedGame));
        if (changeConfiguration) _controller.Configure(true, true, "different audio or video settings");
        else { _game = "Fortnite"; _controller.Tick(); }

        Assert.False(_controller.Running);
        Assert.True(_controller.Saving);
        Assert.Equal("Counter-Strike 2", savedGame);
        Assert.Equal(new[] { "Counter-Strike 2 frame" }, _capture.Frames);
        Assert.Single(_capture.Starts);

        _controller.EndSave();

        Assert.True(_controller.Running);
        Assert.Empty(_capture.Frames);
        Assert.Equal(new[] { true, true }, _capture.Starts);
        Assert.Equal(changeConfiguration ? "Counter-Strike 2" : "Fortnite", _controller.BufferGame);
    }

    [Fact]
    public void Pausing_during_a_save_stops_capture_immediately_and_waits_before_resuming()
    {
        StartGame();
        Assert.True(_controller.TryBeginSave(out _));
        _game = null;
        _controller.Tick();
        Assert.False(_controller.Running);
        Assert.Single(_capture.Frames);
        _game = "Counter-Strike 2";
        _controller.Tick();
        Assert.False(_controller.Running);
        _controller.EndSave();

        Assert.True(_controller.Running);
        Assert.Equal(new[] { true, false }, _capture.Starts);
        Assert.Single(_capture.Frames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabling_or_shutting_down_during_save_stops_now_and_cleans_up_after_save_without_restarting(bool shutdown)
    {
        StartGame();
        Assert.True(_controller.TryBeginSave(out _));
        if (shutdown) _controller.Shutdown();
        else _controller.Configure(false, true, "configuration");
        Assert.False(_controller.Running);
        Assert.Single(_capture.Frames);
        _controller.EndSave();
        _controller.Tick();

        Assert.False(_controller.Running);
        Assert.False(_controller.CanSave);
        Assert.Empty(_capture.Frames);
        Assert.Single(_capture.Starts);
    }

    [Fact]
    public void An_exited_process_is_not_replaced_before_the_watchdog_handles_it_and_the_backoff_expires()
    {
        StartGame();
        _capture.IsRunning = false;
        _controller.Tick();
        Assert.Single(_capture.Starts);
        _controller.CaptureExited(TimeSpan.FromSeconds(5), terminal: false);
        _clock.Seconds = 4;
        _controller.Tick();
        Assert.Single(_capture.Starts);
        _clock.Seconds = 5;
        _controller.Tick();

        Assert.True(_controller.Running);
        Assert.Equal(new[] { true, false }, _capture.Starts);
        Assert.Single(_capture.Frames);
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("disable")]
    [InlineData("shutdown")]
    public void A_pending_restart_cannot_capture_after_leaving_the_game_or_turning_recording_off(string action)
    {
        StartGame();
        _controller.CaptureExited(TimeSpan.FromSeconds(5), terminal: false);
        if (action == "desktop") _game = null;
        else if (action == "disable") _controller.Configure(false, true, "configuration");
        else _controller.Shutdown();
        _clock.Seconds = 10;
        _controller.Tick();

        Assert.False(_controller.Running);
        Assert.Single(_capture.Starts);
    }

    [Fact]
    public void Changing_modes_cancels_an_old_restart_and_starts_only_one_new_capture()
    {
        StartGame();
        _controller.CaptureExited(TimeSpan.FromSeconds(5), terminal: false);
        _game = null;
        _controller.Configure(true, false, "configuration");
        _clock.Seconds = 10;
        _controller.Tick();

        Assert.True(_controller.Running);
        Assert.Equal(new[] { true, true }, _capture.Starts);
        Assert.Null(_controller.BufferGame);
    }

    [Fact]
    public void Losing_focus_during_slow_start_is_checked_again_before_any_frames_are_captured()
    {
        _game = "Counter-Strike 2";
        _capture.BeforeStart = () => _game = null;
        _controller.Configure(true, true, "configuration");
        _controller.Tick();

        Assert.False(_controller.Running);
        Assert.True(_controller.WaitingForGame);
        Assert.Empty(_capture.Starts);
        Assert.Empty(_capture.Frames);
        _capture.BeforeStart = null;
        _game = "Fortnite";
        _controller.Tick();
        Assert.Equal("Fortnite", _controller.BufferGame);
        Assert.Single(_capture.Starts);
    }

    [Fact]
    public void Shutdown_during_start_prevents_the_start_and_all_later_monitor_ticks()
    {
        _game = "Counter-Strike 2";
        _capture.BeforeStart = _controller.Shutdown;
        _controller.Configure(true, true, "configuration");
        _controller.Tick();

        Assert.False(_controller.Running);
        Assert.Empty(_capture.Starts);
    }

    [Fact]
    public void A_failed_start_does_not_spin_and_explicitly_applying_settings_can_retry()
    {
        _game = "Counter-Strike 2";
        _capture.Result = ReplayStartResult.Failed;
        _controller.Configure(true, true, "configuration");
        for (int i = 0; i < 10; i++) _controller.Tick();
        Assert.Equal(1, _capture.Attempts);
        _capture.Result = ReplayStartResult.Started;
        _controller.Configure(true, true, "configuration");

        Assert.True(_controller.Running);
        Assert.Equal(2, _capture.Attempts);
    }

    [Fact]
    public void Detection_failure_pauses_capture_instead_of_falling_back_to_the_desktop()
    {
        bool fail = false;
        var controller = new ReplayRecordingController(_capture, () => fail ? throw new IOException("synthetic process exit") : "Counter-Strike 2");
        controller.Configure(true, true, "configuration");
        _capture.Frames.Add("game frame");
        fail = true;
        controller.Tick();

        Assert.False(controller.Running);
        Assert.True(controller.WaitingForGame);
        Assert.True(controller.CanSave);
        Assert.Single(_capture.Starts);
    }
}