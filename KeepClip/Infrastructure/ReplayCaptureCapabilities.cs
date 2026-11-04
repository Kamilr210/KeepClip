namespace KeepClip.Infrastructure;

internal static class ReplayCaptureCapabilities
{
    internal static void RequireSafeGdi(MediaProcessRunner runner)
    {
        var result = runner.Run(Config.Ffmpeg, new[] { "-hide_banner", "-h", "demuxer=gdigrab" }, 5);
        var help = result.StdOut + "\n" + result.StdErr;
        if (result.Faulted || result.ExitCode != 0 || !help.Contains("use_captureblt", StringComparison.Ordinal) || !help.Contains("high_precision", StringComparison.Ordinal))
            throw new InvalidOperationException(Strings.Get("replay.gdiToolRequired"));
    }
}
