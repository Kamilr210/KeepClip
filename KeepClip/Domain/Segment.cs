namespace KeepClip.Domain;

/// <summary>A transcript line of a clip (<c>segments</c> table).</summary>
public record Segment(long Id, long ClipId, double StartS, double EndS, string Text);

/// <summary>A full-text search hit: the matching segment joined with its clip and the
/// highlighted snippet + bm25 score from the FTS query.</summary>
public record SegmentWithSnippet(
    long SegmentId, double StartS, double EndS, string Text,
    long ClipId, string Game, string Filename, double Duration,
    long SizeBytes, double Mtime, int HasThumb, int Favorite, string? Storage,
    string Snippet, double Score
);
