namespace KeepClip.Tests.Infrastructure;

public class ModelSelectionTests
{
    [Theory]
    [InlineData(0, "small")]
    [InlineData(2, "small")]
    [InlineData(3, "large-v3-turbo")]
    [InlineData(5, "large-v3-turbo")]
    [InlineData(6, "large-v3")]
    [InlineData(24, "large-v3")]
    public void First_candidate_fits_available_video_memory(long gigabytes, string expected)
    {
        var models = Transcriber.SelectModels(gigabytes * 1024 * 1024 * 1024, 0, null);
        Assert.Equal(expected, models[0]);
        Assert.Equal("small", models[^1]);
    }

    [Theory]
    [InlineData("large-v3", "large-v3-turbo")]
    [InlineData("large-v3-turbo", "small")]
    [InlineData("unknown", "large-v3")]
    public void Previously_crashed_model_and_larger_candidates_are_skipped(string blocked, string expected)
        => Assert.Equal(expected, Transcriber.SelectModels(24L * 1024 * 1024 * 1024, 0, blocked)[0]);

    [Fact]
    public void A_crash_of_the_smallest_model_does_not_select_it_again()
        => Assert.Empty(Transcriber.SelectModels(24L * 1024 * 1024 * 1024, 0, "small"));

    [Fact]
    public void Runtime_fallback_floor_prevents_retrying_a_model_that_already_failed()
        => Assert.Equal(new[] { "small" }, Transcriber.SelectModels(24L * 1024 * 1024 * 1024, 2, null));
}