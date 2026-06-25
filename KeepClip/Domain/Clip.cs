namespace KeepClip.Domain;

/// <summary>A video clip in the library. Mirrors a row of the <c>clips</c> table.
/// Mutable bits (Favorite, Storage, RemoteId) are updated via repository methods, not
/// in-place — the record stays an immutable read snapshot.</summary>
public record Clip(
    long Id,
    string Game,
    string Filename,
    double Duration,
    long SizeBytes,
    double Mtime,
    int HasThumb,
    string? TranscribedAt,
    int Favorite,
    string? Storage,
    string? RemoteId,
    string? Filepath
);
