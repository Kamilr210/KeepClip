namespace KeepClip.Tests.Infrastructure;

public class MediaProcessRunnerTests
{
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public void Runner_captures_both_streams_exit_code_and_progress_lines()
    {
        var lines = new List<string>();
        var result = new MediaProcessRunner().Run(PowerShell, new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.WriteLine('progress'); [Console]::Error.WriteLine('diagnostic'); exit 7" }, 10, lines.Add);
        Assert.False(result.Faulted);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("progress", result.StdOut);
        Assert.Contains("diagnostic", result.StdErr);
        Assert.Equal(new[] { "progress" }, lines);
    }

    [Fact]
    public void Missing_executable_is_reported_as_execution_failure()
    {
        var missing = Path.Combine(TestEnvironment.NewTempDir("missing-tool"), "missing.exe");
        var result = new MediaProcessRunner().Run(missing, Array.Empty<string>(), 1);
        Assert.True(result.Faulted);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public void Timed_out_process_is_terminated_and_cannot_write_a_late_marker()
    {
        var marker = Path.Combine(TestEnvironment.NewTempDir("timeout"), "late.txt");
        var script = "[Console]::Out.WriteLine($PID); Start-Sleep -Seconds 10; [IO.File]::WriteAllText('" + marker.Replace("'", "''") + "', 'late')";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var result = new MediaProcessRunner().Run(PowerShell, new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded }, 2);
        Assert.True(result.Faulted);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(File.Exists(marker));
        var pid = int.Parse(result.StdOut.Trim());
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException) { }
    }
}