namespace KeepClip.Tests;

internal sealed class FakeClipOperations : ClipOperations
{
    public Func<string, double?> Duration { get; set; } = _ => throw new InvalidOperationException("Unexpected probe");
    public Func<string, Media.PlaybackVideoInfo?> Video { get; set; } = _ => throw new InvalidOperationException("Unexpected video probe");
    public Func<long, string, bool> Thumbnail { get; set; } = (_, _) => throw new InvalidOperationException("Unexpected thumbnail");
    public Func<string, (bool, string?, string, double)> Fix { get; set; } = _ => throw new InvalidOperationException("Unexpected fix");
    public Func<string, string, double, double, double?, Action<double, string>?, (bool, string, Dictionary<string, object?>)> Cut { get; set; } = (_, _, _, _, _, _) => throw new InvalidOperationException("Unexpected cut");
    public Func<string, string, double, Action<double>?, (bool, string)> Proxy { get; set; } = (_, _, _, _) => throw new InvalidOperationException("Unexpected proxy");
    public Func<string, Action<double, string>?, List<Transcriber.Segment>> Transcription { get; set; } = (_, _) => throw new InvalidOperationException("Unexpected transcription");
    public Func<string, Action<double, string>?, CancellationToken, List<Transcriber.Segment>>? CancellableTranscription { get; set; }
    public Action<string> SendToTrash { get; set; } = _ => throw new InvalidOperationException("Unexpected trash");
    public Func<string, string?> SendToTrashWithRetry { get; set; } = _ => throw new InvalidOperationException("Unexpected trash retry");
    public Func<string, Task> RemoteTrash { get; set; } = _ => throw new InvalidOperationException("Unexpected remote trash");

    public override double? ProbeDuration(string path) => Duration(path);
    public override Media.PlaybackVideoInfo? ProbePlaybackVideo(string path) => Video(path);
    public override bool MakeThumbnail(long clipId, string path) => Thumbnail(clipId, path);
    public override (bool Ok, string? Output, string Message, double Trimmed) FixBrokenClip(string path) => Fix(path);
    public override (bool Ok, string Message, Dictionary<string, object?> Stats) CutClip(string source, string output, double start, double end, double? targetSizeMb, Action<double, string>? progress) => Cut(source, output, start, end, targetSizeMb, progress);
    public override (bool Ok, string Message) CreatePlaybackProxy(string source, string output, double duration, Action<double>? progress) => Proxy(source, output, duration, progress);
    public override List<Transcriber.Segment> Transcribe(string path, Action<double, string>? progress = null, CancellationToken cancellationToken = default) => CancellableTranscription is null ? Transcription(path, progress) : CancellableTranscription(path, progress, cancellationToken);
    public override void TrashFile(string path) => SendToTrash(path);
    public override string? TrashFileWithRetry(string path) => SendToTrashWithRetry(path);
    public override Task TrashRemoteAsync(string remoteId) => RemoteTrash(remoteId);
}