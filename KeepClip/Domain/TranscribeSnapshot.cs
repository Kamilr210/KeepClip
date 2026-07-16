namespace KeepClip.Domain;

public record TranscribeSnapshot(
    bool Running,
    string? CurrentClip,
    int Done,
    int Total,
    double? Percent,
    string? FinishedAt,
    string? Error
);
