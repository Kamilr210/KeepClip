namespace KeepClip.Domain;

/// <summary>A user folder grouping clips (<c>folders</c> + <c>folder_clips</c>).
/// <see cref="SampleClipIds"/> are a few recent member clips for the cover thumbnail.</summary>
public record ClipFolder(
    long Id,
    string Name,
    string CreatedAt,
    int ClipCount,
    IReadOnlyList<long> SampleClipIds
);
