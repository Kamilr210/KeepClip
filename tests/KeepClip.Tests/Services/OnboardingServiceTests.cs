namespace KeepClip.Tests.Services;

public class OnboardingServiceTests
{
    public OnboardingServiceTests() => TestSettings.Reset();

    [Fact]
    public void Fresh_install_has_nothing_started()
    {
        var s = OnboardingService.Get();

        Assert.False(s.started);
        Assert.False(s.completed);
        Assert.False(s.skipped);
        Assert.Null(s.current_step);
        Assert.Equal(0, s.completed_version);
        Assert.Equal(OnboardingService.CurrentVersion, s.current_version);
        Assert.Empty(s.seen_hints);
    }

    [Fact]
    public void Patch_persists_progress_between_reads()
    {
        OnboardingService.Patch(started: true, null, null, "favorites", null);

        var s = OnboardingService.Get();
        Assert.True(s.started);
        Assert.Equal("favorites", s.current_step);
    }

    [Fact]
    public void Blank_step_keeps_the_previous_one()
    {
        OnboardingService.Patch(true, null, null, "cloud", null);

        var s = OnboardingService.Patch(null, null, null, "   ", null);

        Assert.Equal("cloud", s.current_step);
    }

    [Fact]
    public void Completing_stamps_version_and_clears_skip_and_step()
    {
        OnboardingService.Patch(true, null, skipped: true, "cloud", null);

        var s = OnboardingService.Patch(null, completed: true, skipped: true, "done", null);

        Assert.True(s.completed);
        Assert.False(s.skipped);
        Assert.Null(s.current_step);
        Assert.Equal(OnboardingService.CurrentVersion, s.completed_version);
    }

    [Fact]
    public void Skipping_clears_the_step_but_not_completion_history()
    {
        OnboardingService.Patch(true, completed: true, null, null, null);

        var s = OnboardingService.Patch(null, completed: false, skipped: true, "cloud", null);

        Assert.True(s.skipped);
        Assert.Null(s.current_step);
        Assert.Equal(OnboardingService.CurrentVersion, s.completed_version);
    }

    [Fact]
    public void Restart_keeps_completion_but_drops_skip_and_step()
    {
        OnboardingService.Patch(true, completed: true, null, null, null);
        OnboardingService.Patch(null, completed: false, skipped: true, "cloud", null);

        var s = OnboardingService.Restart();

        Assert.True(s.started);
        Assert.False(s.skipped);
        Assert.Null(s.current_step);
        Assert.Equal(OnboardingService.CurrentVersion, s.completed_version);
    }

    [Fact]
    public void Seen_hints_are_deduplicated_and_capped()
    {
        OnboardingService.Patch(null, null, null, null, "share-v1");
        OnboardingService.Patch(null, null, null, null, " share-v1 ");
        Assert.Equal(new[] { "share-v1" }, OnboardingService.Get().seen_hints);

        for (int i = 0; i < 205; i++) OnboardingService.Patch(null, null, null, null, $"h{i}");

        var hints = OnboardingService.Get().seen_hints;
        Assert.Equal(200, hints.Count);
        Assert.DoesNotContain("share-v1", hints);
        Assert.Equal("h204", hints[^1]);
    }

    [Fact]
    public void State_survives_alongside_other_settings()
    {
        Settings.SetLanguage("pl");
        OnboardingService.Patch(true, null, null, null, null);
        Settings.SetString("accent_theme", "green");

        Assert.True(OnboardingService.Get().started);
        Assert.Equal("pl", Settings.GetLanguage());
        Assert.Equal("green", Settings.GetString("accent_theme"));
    }
}
