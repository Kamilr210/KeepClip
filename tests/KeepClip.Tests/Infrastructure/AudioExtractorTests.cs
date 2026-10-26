using System.Diagnostics;
using System.Text;

namespace KeepClip.Tests.Infrastructure;

public class AudioExtractorTests
{
    private readonly string _temporary = TestEnvironment.NewTempDir("audio-extraction");

    private sealed class ChunkedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    [Fact]
    public async Task Decoder_preserves_signed_pcm_samples_even_when_reads_split_samples()
    {
        using var stream = new ChunkedStream(new byte[] { 0, 128, 0, 0, 255, 127 });
        var result = await AudioExtractor.ReadSamplesAsync(stream, _temporary, 3, CancellationToken.None);
        Assert.Equal(new[] { -1f, 0f, 32767 / 32768f }, result);
        Assert.Empty(Directory.EnumerateFiles(_temporary));
    }

    [Fact]
    public async Task Decoder_reads_at_most_the_budget_plus_one_byte_and_never_returns_partial_audio()
    {
        using var stream = new MemoryStream(new byte[1_000_000]);
        await Assert.ThrowsAsync<InvalidDataException>(() => AudioExtractor.ReadSamplesAsync(stream, _temporary, 4, CancellationToken.None));
        Assert.Equal(9, stream.Position);
        Assert.Empty(Directory.EnumerateFiles(_temporary));
    }

    [Fact]
    public async Task Decoder_rejects_an_incomplete_sample_and_accepts_empty_audio_as_no_result()
    {
        using var broken = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<InvalidDataException>(() => AudioExtractor.ReadSamplesAsync(broken, _temporary, 4, CancellationToken.None));
        using var empty = new MemoryStream();
        Assert.Null(await AudioExtractor.ReadSamplesAsync(empty, _temporary, 4, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_temporary));
    }

    [Fact]
    public async Task Budget_failure_terminates_a_producer_instead_of_waiting_for_its_completion()
    {
        var pidPath = Path.Combine(_temporary, "producer.pid");
        var marker = Path.Combine(_temporary, "unexpected.txt");
        var script = $"[IO.File]::WriteAllText('{Escape(pidPath)}',[string]$PID); $stream=[Console]::OpenStandardOutput(); $bytes=New-Object byte[] 65536; $stream.Write($bytes,0,$bytes.Length); $stream.Flush(); Start-Sleep -Seconds 30; [IO.File]::WriteAllText('{Escape(marker)}','late')";
        await Assert.ThrowsAsync<InvalidDataException>(() => AudioExtractor.DecodeAsync(Producer(script), _temporary, 4, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        AssertStopped(int.Parse(File.ReadAllText(pidPath)));
        Assert.False(File.Exists(marker));
        Assert.Empty(Directory.EnumerateFiles(_temporary, "*.pcm"));
    }

    [Fact]
    public async Task Cancellation_stops_a_producer_blocked_without_audio_output()
    {
        var pidPath = Path.Combine(_temporary, "producer.pid");
        using var cts = new CancellationTokenSource();
        var task = AudioExtractor.DecodeAsync(Producer($"[IO.File]::WriteAllText('{Escape(pidPath)}',[string]$PID); Start-Sleep -Seconds 30"), _temporary, 4, cts.Token);
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(pidPath)) await Task.Delay(20, wait.Token);
        }
        finally { cts.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        AssertStopped(int.Parse(File.ReadAllText(pidPath)));
        Assert.Empty(Directory.EnumerateFiles(_temporary, "*.pcm"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Process_exit_status_determines_whether_decoded_samples_are_accepted(int exitCode, bool success)
    {
        var result = await AudioExtractor.DecodeAsync(Producer($"$stream=[Console]::OpenStandardOutput(); $stream.WriteByte(0); $stream.WriteByte(64); $stream.Flush(); exit {exitCode}"), _temporary, 4, CancellationToken.None);
        if (success) Assert.Equal(new[] { 0.5f }, result);
        else Assert.Null(result);
        Assert.Empty(Directory.EnumerateFiles(_temporary, "*.pcm"));
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static ProcessStartInfo Producer(string script)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"));
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(arg);
        return info;
    }

    private static void AssertStopped(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }
}