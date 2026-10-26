using System.Buffers.Binary;
using System.Diagnostics;

namespace KeepClip.Infrastructure;

internal static class AudioExtractor
{
    // 30 minut, mono 16 kHz: maksymalnie 57,6 MB PCM na dysku i 115,2 MB próbek w pamięci.
    internal const int MaximumSamples = 16000 * 30 * 60;

    internal static Task<float[]?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo { FileName = Config.Ffmpeg };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", path, "-af", "dynaudnorm=f=150:g=15", "-vn", "-ac", "1", "-ar", "16000", "-f", "s16le", "-" })
            info.ArgumentList.Add(arg);
        return DecodeAsync(info, Config.TmpDir, MaximumSamples, cancellationToken);
    }

    internal static async Task<float[]?> DecodeAsync(ProcessStartInfo info, string temporaryDirectory, int maximumSamples, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var stop = deadline.Token.Register(() => Kill(process));
        var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
        try
        {
            var samples = await ReadSamplesAsync(process.StandardOutput.BaseStream, temporaryDirectory, maximumSamples, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await errors;
            return process.ExitCode == 0 ? samples : null;
        }
        finally
        {
            Kill(process);
            deadline.Cancel();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            try { await errors; } catch (OperationCanceledException) { } catch (IOException) { }
        }
    }

    internal static async Task<float[]?> ReadSamplesAsync(Stream input, string temporaryDirectory, int maximumSamples, CancellationToken cancellationToken)
    {
        if (maximumSamples is < 1 or > MaximumSamples) throw new ArgumentOutOfRangeException(nameof(maximumSamples));
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, $"audio-{Guid.NewGuid():N}.pcm");
        await using var pcm = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 32768, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        var buffer = new byte[32768];
        long maximumBytes = maximumSamples * 2L;
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - pcm.Length + 1)), cancellationToken);
            if (read == 0) break;
            if (pcm.Length + read > maximumBytes) throw new InvalidDataException("Audio przekracza limit długości transkrypcji (30 minut mono 16 kHz).");
            await pcm.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (pcm.Length == 0) return null;
        if (pcm.Length % 2 != 0) throw new InvalidDataException("Niekompletna próbka zdekodowanego audio.");
        cancellationToken.ThrowIfCancellationRequested();
        var samples = new float[(int)(pcm.Length / 2)];
        pcm.Position = 0;
        int offset = 0;
        while (pcm.Position < pcm.Length)
        {
            int count = (int)Math.Min(buffer.Length, pcm.Length - pcm.Position);
            await pcm.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken);
            for (int i = 0; i < count; i += 2) samples[offset++] = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2)) / 32768f;
        }
        return samples;
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}