namespace KeepClip.Domain;

/// <summary>The result of a transcript search: the echoed query, the FTS expression it was
/// translated to, and the ranked hits.</summary>
public record SearchResult(string Query, string Fts, IReadOnlyList<SegmentWithSnippet> Results);
