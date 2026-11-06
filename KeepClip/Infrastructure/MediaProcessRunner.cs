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
        Action<string>? onOutputLine = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        try
        {
            proc.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            if (proc.WaitForExit(5000)) proc.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            return new MediaRunResult(true, -1, so.ToString(), se.ToString());
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new MediaRunResult(false, proc.ExitCode, so.ToString(), se.ToString());
    }
}