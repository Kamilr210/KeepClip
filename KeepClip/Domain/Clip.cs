namespace KeepClip.Domain;

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
