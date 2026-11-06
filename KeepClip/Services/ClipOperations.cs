namespace KeepClip.Services;

// Granica operacji wykonywanych przez narzędzia systemowe i usługi zewnętrzne.
internal class ClipOperations
{
    public virtual double? ProbeDuration(string path) => Media.ProbeDuration(path);
    public virtual Media.PlaybackVideoInfo? ProbePlaybackVideo(string path) => Media.ProbePlaybackVideo(path);
    public virtual bool MakeThumbnail(long clipId, string path, double? duration = null) => Media.MakeThumbnail(clipId, path, duration: duration);
    public virtual (bool Ok, string? Output, string Message, double Trimmed) FixBrokenClip(string path) => Media.FixBrokenClip(path);
    public virtual (bool Ok, string Message, Dictionary<string, object?> Stats) CutClip(string source, string output, double start, double end, double? targetSizeMb, Action<double, string>? progress) => Media.CutClip(source, output, start, end, targetSizeMb, progress);
    public virtual (bool Ok, string Message) CreatePlaybackProxy(string source, string output, double duration, Action<double>? progress, CancellationToken cancellationToken = default, Media.PlaybackVideoInfo? videoInfo = null) => Media.CreatePlaybackProxy(source, output, duration, progress, cancellationToken, videoInfo);
    public virtual List<Transcriber.Segment> Transcribe(string path, Action<double, string>? progress = null, CancellationToken cancellationToken = default) => Transcriber.Transcribe(path, progress, cancellationToken);
    public virtual void TrashFile(string path) => Trash.Send(path);
    public virtual string? TrashFileWithRetry(string path) => Trash.SendWithRetry(path);
    public virtual Task TrashRemoteAsync(string remoteId) => CloudService.TrashRemoteAsync(remoteId);
}