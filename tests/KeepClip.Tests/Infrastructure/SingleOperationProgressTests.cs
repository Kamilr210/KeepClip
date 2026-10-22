namespace KeepClip.Tests.Infrastructure;

public class SingleOperationProgressTests
{
    [Fact]
    public void New_operation_resets_previous_result_and_ignores_stale_callbacks()
    {
        var old = SingleOperationProgress.Start("cut", 1);
        SingleOperationProgress.Finish(old, false, "old failure");
        var current = SingleOperationProgress.Start("transcription", 2);
        SingleOperationProgress.Report(old, 0.9, "stale");
        SingleOperationProgress.Finish(old, true);

        var status = SingleOperationProgress.Snapshot();
        Assert.NotEqual(old, current);
        Assert.Equal(current, status["id"]);
        Assert.Equal(2L, status["clip_id"]);
        Assert.Equal("transcription", status["kind"]);
        Assert.Equal("preparing", status["stage"]);
        Assert.Equal(0.0, status["progress"]);
        Assert.Equal(true, status["active"]);
        Assert.Null(status["success"]);
        Assert.Null(status["error"]);
        SingleOperationProgress.Finish(current, true);
    }

    [Fact]
    public void Progress_is_monotonic_and_reaches_one_only_on_success()
    {
        var id = SingleOperationProgress.Start("cut", 1);
        SingleOperationProgress.Report(id, 0.6, "cutting");
        SingleOperationProgress.Report(id, 0.2, "retry");
        Assert.Equal(0.6, SingleOperationProgress.Snapshot()["progress"]);
        SingleOperationProgress.Report(id, 10, "saving");
        Assert.Equal(0.99, SingleOperationProgress.Snapshot()["progress"]);
        SingleOperationProgress.Finish(id, true);
        SingleOperationProgress.Report(id, 0.1, "late");
        Assert.Equal(1.0, SingleOperationProgress.Snapshot()["progress"]);
        Assert.Equal(false, SingleOperationProgress.Snapshot()["active"]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_progress_cannot_corrupt_the_status_response(double progress)
    {
        var id = SingleOperationProgress.Start("cut", 1);
        SingleOperationProgress.Report(id, 0.3, "cutting");
        SingleOperationProgress.Report(id, progress, "invalid");
        var status = SingleOperationProgress.Snapshot();
        Assert.Equal(0.3, status["progress"]);
        Assert.Equal("cutting", status["stage"]);
        Assert.NotEmpty(System.Text.Json.JsonSerializer.Serialize(status));
        SingleOperationProgress.Finish(id, false, "test completed");
    }

    [Fact]
    public void First_terminal_result_cannot_be_overwritten_by_a_late_completion()
    {
        var id = SingleOperationProgress.Start("cut", 1);
        SingleOperationProgress.Report(id, 0.4, "cutting");
        SingleOperationProgress.Finish(id, false, "encoder failed");
        SingleOperationProgress.Finish(id, true);
        var status = SingleOperationProgress.Snapshot();
        Assert.Equal(false, status["success"]);
        Assert.Equal("encoder failed", status["error"]);
        Assert.Equal(0.4, status["progress"]);
    }

    [Fact]
    public void Snapshots_are_independent_copies()
    {
        var id = SingleOperationProgress.Start("cut", 1);
        var snapshot = SingleOperationProgress.Snapshot();
        snapshot["progress"] = 999;
        Assert.Equal(0.0, SingleOperationProgress.Snapshot()["progress"]);
        SingleOperationProgress.Finish(id, true);
    }
}