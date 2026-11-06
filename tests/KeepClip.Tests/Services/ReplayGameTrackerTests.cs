namespace KeepClip.Tests.Services;

public class ReplayGameTrackerTests
{
    private sealed class Clock : TimeProvider
    {
        internal long Seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }

    [Theory]
    [InlineData("KeepClip")]
    [InlineData("explorer")]
    [InlineData("chrome")]
    [InlineData("Discord")]
    [InlineData("steam")]
    public void Saving_after_switching_away_from_the_game_keeps_its_folder(string desktopProcess)
    {
        var clock = new Clock();
        string process = "cs2";
        var tracker = new ReplayGameTracker(() => DisplayHelper.GameName(process, () => null), clock);
        tracker.Observe();

        process = desktopProcess;
        clock.Seconds = 10;
        tracker.Observe();

        Assert.Equal("Counter-Strike 2", tracker.Resolve(120));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(120)]
    [InlineData(600)]
    public void A_game_is_forgotten_when_it_is_no_longer_in_the_replay_buffer(int durationSeconds)
    {
        var clock = new Clock();
        string? foreground = "Counter-Strike 2";
        var tracker = new ReplayGameTracker(() => foreground, clock);
        tracker.Observe();
        clock.Seconds = 10;
        tracker.Observe();

        foreground = null;
        clock.Seconds += durationSeconds;
        Assert.Equal("Counter-Strike 2", tracker.Resolve(durationSeconds));
        clock.Seconds++;
        Assert.Equal("Pulpit", tracker.Resolve(durationSeconds));
    }

    [Fact]
    public void The_game_active_at_save_time_replaces_the_previous_game_without_waiting_for_a_poll()
    {
        string? foreground = "Counter-Strike 2";
        var tracker = new ReplayGameTracker(() => foreground);
        Assert.Equal("Counter-Strike 2", tracker.Resolve(120));

        foreground = "Another game";
        Assert.Equal("Another game", tracker.Resolve(120));
        foreground = null;
        Assert.Equal("Another game", tracker.Resolve(120));
    }

    [Fact]
    public void An_empty_or_reset_buffer_does_not_reuse_a_game_from_the_previous_recording()
    {
        string? foreground = null;
        var tracker = new ReplayGameTracker(() => foreground);
        Assert.Equal("Pulpit", tracker.Resolve(120));
        foreground = "Counter-Strike 2";
        tracker.Observe();

        tracker.Reset();
        foreground = null;

        Assert.Equal("Pulpit", tracker.Resolve(120));
    }
}