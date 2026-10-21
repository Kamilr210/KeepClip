#if KEEPCLIP_DEV
namespace KeepClip.Tests.Infrastructure;

public class DevResourceMonitorTests
{
    [Fact]
    public void Selects_only_own_webview_and_media_processes()
    {
        var entries = new[]
        {
            new DevResourceMonitor.ProcessEntry(1, 0, "KeepClip.exe"),
            new DevResourceMonitor.ProcessEntry(12, 11, "msedgewebview2.exe"),
            new DevResourceMonitor.ProcessEntry(11, 1, "msedgewebview2.exe"),
            new DevResourceMonitor.ProcessEntry(13, 11, "crashpad_handler.exe"),
            new DevResourceMonitor.ProcessEntry(20, 2, "msedgewebview2.exe"),
            new DevResourceMonitor.ProcessEntry(30, 1, "ffmpeg.exe"),
            new DevResourceMonitor.ProcessEntry(31, 30, "ffprobe.exe"),
            new DevResourceMonitor.ProcessEntry(32, 2, "ffmpeg.exe"),
            new DevResourceMonitor.ProcessEntry(40, 1, "chrome.exe"),
            new DevResourceMonitor.ProcessEntry(41, 40, "msedgewebview2.exe"),
            new DevResourceMonitor.ProcessEntry(50, 0, "msedgewebview2.exe"),
            new DevResourceMonitor.ProcessEntry(51, 50, "msedgewebview2.exe"),
            new DevResourceMonitor.ProcessEntry(60, 0, "other.exe"),
        };
        var selected = DevResourceMonitor.SelectProcesses(1, entries, new Dictionary<int, string> { [50] = "Browser", [60] = "Renderer" });

        Assert.Equal(new[] { 1, 11, 12, 13, 30, 31, 50, 51 }, selected.Keys.Order().ToArray());
        Assert.Equal("KeepClip / Whisper", selected[1]);
        Assert.Equal("WebView2 / Browser", selected[50]);
    }

    [Fact]
    public void Cpu_is_normalized_to_whole_machine_and_io_uses_elapsed_time()
    {
        var previous = new DevResourceMonitor.ProcessSample(100, TimeSpan.TicksPerSecond, 1, 100, 200);
        var current = new DevResourceMonitor.ProcessSample(100, 2 * TimeSpan.TicksPerSecond, 1, 1100, 2200);

        Assert.Equal(12.5, DevResourceMonitor.CpuPercent(current, previous, 2, 4));
        Assert.Equal(500, DevResourceMonitor.Rate(current, previous, 2, false));
        Assert.Equal(1000, DevResourceMonitor.Rate(current, previous, 2, true));
    }

    [Fact]
    public void New_or_reused_process_and_missing_counters_do_not_report_fake_rates()
    {
        var previous = new DevResourceMonitor.ProcessSample(100, 1000, 1, 100, 200);
        var current = new DevResourceMonitor.ProcessSample(200, 2000, 1, 1000, 2000);

        Assert.Null(DevResourceMonitor.CpuPercent(current, null, 2, 4));
        Assert.Null(DevResourceMonitor.CpuPercent(current, previous, 2, 4));
        Assert.Null(DevResourceMonitor.Rate(current, previous, 2, false));
        Assert.Null(DevResourceMonitor.CpuPercent(previous, previous, 0, 4));
        Assert.Null(DevResourceMonitor.Rate(previous with { ReadBytes = null }, previous, 2, false));
        Assert.Null(DevResourceMonitor.Rate(previous with { ReadBytes = 50 }, previous, 2, false));
    }

    [Fact]
    public void Gpu_aggregates_processes_on_same_engine_but_does_not_add_different_engines()
    {
        var engines = new[]
        {
            new DevGpuCounters.Engine(1, "gpu0_eng0", 30),
            new DevGpuCounters.Engine(2, "gpu0_eng0", 20),
            new DevGpuCounters.Engine(1, "gpu0_eng1", 80),
            new DevGpuCounters.Engine(1, "gpu1_eng0", 40),
        };
        Assert.Equal(80, DevGpuCounters.BusiestEngine(engines));
        Assert.Equal(100, DevGpuCounters.BusiestEngine(new[] { new DevGpuCounters.Engine(1, "same", 70), new DevGpuCounters.Engine(2, "same", 60) }));
    }

    [Fact]
    public void Gpu_counter_identity_retains_adapter_and_engine()
    {
        Assert.True(DevGpuCounters.TryPid("pid_123_luid_0x00000000_0x00012345_phys_0_eng_1_engtype_Compute", out var pid, out var key));
        Assert.Equal(123, pid);
        Assert.Equal("luid_0x00000000_0x00012345_phys_0_eng_1_engtype_Compute", key);
        Assert.False(DevGpuCounters.TryPid("_Total", out _, out _));
        Assert.False(DevGpuCounters.TryPid("pid_bad_eng_0", out _, out _));
    }
}
#endif
