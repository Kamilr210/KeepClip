using System.Diagnostics;
using System.Text;

namespace KeepClip.Infrastructure;

internal readonly record struct MediaRunResult(bool Faulted, int ExitCode, string StdOut, string StdErr);

internal class MediaProcessRunner
{
    public virtual MediaRunResult Run(
        string exe,
        IReadOnlyList<string> args,
        int timeoutSeconds,
        Action<string>? onOutputLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var so = new StringBuilder();
        var se = new StringBuilder();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            so.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
        };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };

        try { proc.Start(); }
        catch { return new MediaRunResult(true, -1, "", ""); }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new MediaRunResult(true, -1, so.ToString(), se.ToString());
        }
        proc.WaitForExit();
        return new MediaRunResult(false, proc.ExitCode, so.ToString(), se.ToString());
    }
}