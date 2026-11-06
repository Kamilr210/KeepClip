namespace KeepClip.Services;

internal sealed record ReplayCaptureFormat(int Tier, string? Encoder, int Width, int Height, int Fps, ReplayAudioFormat? Audio);

internal sealed class ReplayBuffer(string directory)
{
    private string? _sealedSegment;
    private long _sealedLength;
    private DateTime _sealedWriteTime;
    internal ReplayCaptureFormat? Format { get; private set; }
    internal bool HasSegments => Format is not null && Directory.Exists(directory) && Directory.EnumerateFiles(directory, "seg_*.ts").Any();

    internal bool Prepare(bool freshRing, ReplayCaptureFormat format, Func<bool> stillAllowed)
    {
        if (!stillAllowed()) return false;
        Directory.CreateDirectory(directory);
        if (freshRing || Format is null || Format != format) Clear();
        else Task.Run(SealLastSegmentAsync).GetAwaiter().GetResult();
        return stillAllowed();
    }

    internal void Accept(ReplayCaptureFormat format) => Format = format;

    internal void Clear()
    {
        if (Directory.Exists(directory))
            foreach (var file in Directory.GetFiles(directory, "seg_*.ts")) File.Delete(file);
        Format = null;
        _sealedSegment = null;
    }

    private async Task SealLastSegmentAsync()
    {
        var current = Directory.GetFiles(directory, "seg_*.ts").Order(StringComparer.Ordinal).LastOrDefault();
        if (current is null) return;
        var currentInfo = new FileInfo(current);
        if (current == _sealedSegment && currentInfo.Length == _sealedLength && currentInfo.LastWriteTimeUtc == _sealedWriteTime) return;
        var snapshot = Path.Combine(directory, $"sealed_{Guid.NewGuid():N}.part");
        try
        {
            using (var source = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = File.Create(snapshot))
                await ReplaySegmentSnapshot.CopyAsync(source, target, recording: true);
            if (new FileInfo(snapshot).Length == 0) { File.Delete(current); _sealedSegment = null; }
            else
            {
                File.Move(snapshot, current, overwrite: true);
                currentInfo.Refresh();
                _sealedSegment = current;
                _sealedLength = currentInfo.Length;
                _sealedWriteTime = currentInfo.LastWriteTimeUtc;
            }
        }
        finally
        {
            try { if (File.Exists(snapshot)) File.Delete(snapshot); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}