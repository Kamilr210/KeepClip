namespace KeepClip.Domain;

public record ClipFolder(
    long Id,
    string Name,
    string CreatedAt,
    int ClipCount,
    IReadOnlyList<long> SampleClipIds
);
