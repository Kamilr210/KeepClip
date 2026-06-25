namespace KeepClip.Domain;

/// <summary>A point-in-time view of the transcription worker for the status/SSE endpoints.</summary>
public record TranscribeSnapshot(
    bool Running,
    string? CurrentClip,
    int Done,
    int Total,
    double? Percent,
    string? FinishedAt,
    string? Error
);
