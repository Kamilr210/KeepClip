namespace KeepClip.Tests.Services;

public class CloudMetadataTests
{
    [Theory]
    [InlineData("CS2 2026-10-22 12-34-56.mp4", "Counter-Strike 2")]
    [InlineData("Elden Ring 2026-10-22_12.34.56.mkv", "Elden Ring")]
    [InlineData("Zażółć 2026-10-22-12:34:56.webm", "Zażółć")]
    [InlineData("Gra 2 2026-10-22 12-34-56.mp4", "Gra 2")]
    [InlineData("CON 2026-10-22 12-34-56.mp4", "_CON")]
    [InlineData("clip.mp4", "Chmura")]
    [InlineData("CS2 2026-10-22 12-34-X.mp4", "Chmura")]
    [InlineData("2026-10-22 12-34-56.mp4", "Chmura")]
    public void Game_is_detected_from_a_supported_recording_name(string name, string expected)
        => Assert.Equal(expected, CloudService.RemoteGame(File(name)));

    [Theory]
    [InlineData("_root", "_root")]
    [InlineData("Elden Ring", "Elden Ring")]
    [InlineData("../outside", ".._outside")]
    [InlineData("C:\\outside", "C__outside")]
    [InlineData("NUL", "_NUL")]
    [InlineData("..", "Pulpit")]
    public void Explicit_game_metadata_is_preserved_and_sanitized(string game, string expected)
        => Assert.Equal(expected, CloudService.RemoteGame(File("CS2 2026-10-22 12-34-56.mp4", game)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_game_metadata_falls_back_to_the_filename(string? game)
        => Assert.Equal("Counter-Strike 2", CloudService.RemoteGame(File("CS2 2026-10-22 12-34-56.mp4", game)));

    [Fact]
    public void Oversized_remote_names_use_the_fallback_without_scanning_the_whole_name()
        => Assert.Equal("Chmura", CloudService.RemoteGame(File(new string(' ', 100_000) + "2026-10-22 12-34-X.mp4")));

    [Fact]
    public void Long_near_matches_are_treated_as_unrecognized_names()
        => Assert.Equal("Chmura", CloudService.RemoteGame(File("Gra" + new string(' ', 220) + "2026-10-22 12-34-X.mp4")));

    private static GoogleDrive.DriveFile File(string name, string? game = null)
    {
        var properties = new Dictionary<string, string>();
        if (game is not null) properties["game"] = game;
        return new("test-id", name, "video/mp4", 100, null, null, properties);
    }
}