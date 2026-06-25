namespace KeepClip.Domain;

/// <summary>Library-wide statistics for the dashboard cards.</summary>
public record Stats(
    long Clips,
    long Transcribed,
    long Segments,
    long TotalBytes,
    long LocalBytes,
    long CloudBytes,
    long Favorites,
    long? DiskTotal,
    long? DiskFree,
    IReadOnlyList<GameStat> Games
);

/// <summary>Per-game clip counts (total + transcribed).</summary>
public record GameStat(string Game, long Clips, long Done);
