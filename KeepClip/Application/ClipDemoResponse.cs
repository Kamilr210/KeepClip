namespace KeepClip.Application;

public sealed record ClipDemoResponse(long clip_id, string? path, string? filename, bool available, long? size_bytes);