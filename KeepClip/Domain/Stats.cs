namespace KeepClip.Domain;

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

public record GameStat(string Game, long Clips, long Done);
