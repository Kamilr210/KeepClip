namespace KeepClip.Tests.Infrastructure;

public class LogStateTests
{
    [Fact]
    public void Transcription_log_is_bounded_and_snapshots_do_not_share_the_log_list()
    {
        var prefix = Guid.NewGuid().ToString("N");
        for (int i = 0; i < 110; i++) TranscribeState.PushLog(prefix + i);
        var snapshot = TranscribeState.Snapshot();
        Assert.Equal(Enumerable.Range(102, 8).Select(i => prefix + i), snapshot.log_tail);
        snapshot.log_tail.Clear();
        Assert.Equal(8, TranscribeState.Snapshot().log_tail.Count);
    }

#if KEEPCLIP_DEV
    [Fact]
    public void Developer_log_supports_incremental_reads_and_limits_history()
    {
        DevLog.Clear();
        var before = DevLog.GetSince(0).seq;
        for (int i = 0; i < 4005; i++) DevLog.Add("test-log-" + i);
        var result = DevLog.GetSince(before);
        Assert.Equal(4000, result.lines.Length);
        Assert.EndsWith("test-log-5", result.lines[0]);
        Assert.EndsWith("test-log-4004", result.lines[^1]);
        Assert.Empty(DevLog.GetSince(result.seq).lines);
        DevLog.Clear();
        Assert.Single(DevLog.GetSince(result.seq).lines);
    }

    [Fact]
    public void Console_error_tee_keeps_original_output_and_collects_complete_lines()
    {
        var original = Console.Error;
        using var sink = new StringWriter();
        var before = DevLog.GetSince(0).seq;
        try
        {
            Console.SetError(sink);
            DevLog.Install();
            Console.Error.Write('a');
            Console.Error.Write('\r');
            Console.Error.Write('\n');
            Console.Error.Write("b\r\nc");
            Console.Error.WriteLine("d");
            Console.Error.Write((string?)null);
            Console.Error.WriteLine("");
        }
        finally { Console.SetError(original); }
        Assert.Contains("a\r\nb\r\ncd", sink.ToString());
        var lines = DevLog.GetSince(before).lines;
        Assert.Contains(lines, line => line.EndsWith("  a"));
        Assert.Contains(lines, line => line.EndsWith("  b"));
        Assert.Contains(lines, line => line.EndsWith("  c"));
        Assert.Contains(lines, line => line.EndsWith("  d"));
    }
#endif
}