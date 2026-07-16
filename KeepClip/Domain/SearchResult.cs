namespace KeepClip.Domain;

public record SearchResult(string Query, string Fts, IReadOnlyList<SegmentWithSnippet> Results);
