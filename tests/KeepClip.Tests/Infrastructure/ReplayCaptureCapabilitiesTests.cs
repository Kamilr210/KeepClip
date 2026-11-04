namespace KeepClip.Tests.Infrastructure;

public class ReplayCaptureCapabilitiesTests
{
    private sealed class Runner(MediaRunResult result) : MediaProcessRunner
    {
        public override MediaRunResult Run(string exe, IReadOnlyList<string> args, int timeoutSeconds, Action<string>? onOutputLine = null)
        {
            Assert.Equal(Config.Ffmpeg, exe);
            Assert.Equal(new[] { "-hide_banner", "-h", "demuxer=gdigrab" }, args);
            Assert.Equal(5, timeoutSeconds);
            return result;
        }
    }

    [Theory]
    [InlineData("use_captureblt high_precision", "")]
    [InlineData("", "use_captureblt high_precision")]
    public void Patched_tool_can_report_its_options_on_either_stream(string stdout, string stderr)
        => ReplayCaptureCapabilities.RequireSafeGdi(new Runner(new(false, 0, stdout, stderr)));

    [Theory]
    [InlineData(false, 0, "draw_mouse framerate")]
    [InlineData(false, 0, "use_captureblt")]
    [InlineData(false, 0, "high_precision")]
    [InlineData(false, 1, "use_captureblt high_precision")]
    [InlineData(true, -1, "use_captureblt high_precision")]
    public void Old_or_unavailable_tool_is_rejected_before_capture_can_start(bool faulted, int exitCode, string help)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReplayCaptureCapabilities.RequireSafeGdi(new Runner(new(faulted, exitCode, help, ""))));
        Assert.Equal(Strings.Get("replay.gdiToolRequired"), error.Message);
    }
}
