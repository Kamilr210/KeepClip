namespace KeepClip.Domain;

public record Segment(long Id, long ClipId, double StartS, double EndS, string Text);

public record SegmentWithSnippet(
    long SegmentId, double StartS, double EndS, string Text,
    long ClipId, string Game, string Filename, double Duration,
    long SizeBytes, double Mtime, int HasThumb, int Favorite, string? Storage,
    string Snippet, double Score
);
