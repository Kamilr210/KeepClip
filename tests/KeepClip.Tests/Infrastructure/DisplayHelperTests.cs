namespace KeepClip.Tests.Infrastructure;

public class DisplayHelperTests
{
    [Theory]
    [InlineData("notepad")]
    [InlineData("Code")]
    [InlineData("unknown-game")]
    public void Games_only_requires_installation_evidence_and_never_guesses_from_product_metadata(string process)
        => Assert.Null(DisplayHelper.GameName(process, () => throw new InvalidOperationException("Product metadata cannot confirm a game"), () => null, confirmedOnly: true));

    [Theory]
    [InlineData("cs2", "Counter-Strike 2")]
    [InlineData("VALORANT-Win64-Shipping", "VALORANT")]
    public void Games_only_accepts_known_game_processes(string process, string expected)
        => Assert.Equal(expected, DisplayHelper.GameName(process, () => null, confirmedOnly: true));

    [Fact]
    public void Games_only_accepts_a_title_confirmed_by_its_installation()
        => Assert.Equal("Installed Game", DisplayHelper.GameName("Game-Win64-Shipping", () => null, () => "Installed Game", confirmedOnly: true));

    [Theory]
    [InlineData("obs64")]
    [InlineData("wallpaper64")]
    [InlineData("blender")]
    [InlineData("UnrealEditor")]
    [InlineData("Unity")]
    public void Desktop_software_does_not_enable_capture_even_when_installed_through_a_game_store(string process)
        => Assert.Null(DisplayHelper.GameName(process, () => null, () => "Installed Application", confirmedOnly: true));

    [Theory]
    [InlineData("cs2", "Counter-Strike 2")]
    [InlineData("CS2", "Counter-Strike 2")]
    [InlineData("FortniteClient-Win64-Shipping", "Fortnite")]
    [InlineData("VALORANT-Win64-Shipping", "VALORANT")]
    [InlineData("League of Legends", "League of Legends")]
    public void Known_games_use_a_stable_folder_without_reading_executable_metadata(string processName, string expected)
        => Assert.Equal(expected, DisplayHelper.GameName(processName, () => throw new InvalidOperationException("Metadata is not needed")));

    [Theory]
    [InlineData("KeepClip")]
    [InlineData("msedgewebview2")]
    [InlineData("explorer")]
    [InlineData("CHROME")]
    [InlineData("msedge")]
    [InlineData("firefox")]
    [InlineData("Discord")]
    [InlineData("steam")]
    [InlineData("steamwebhelper")]
    [InlineData("EpicGamesLauncher")]
    [InlineData("GameBar")]
    [InlineData("SearchHost")]
    [InlineData("LeagueClientUx")]
    [InlineData("CrashReportClient")]
    [InlineData("FortniteLauncher")]
    public void Desktop_apps_and_overlays_do_not_replace_the_recorded_game(string processName)
        => Assert.Null(DisplayHelper.GameName(processName, () => "Desktop application"));

    [Theory]
    [InlineData("custom-game", "  Test: Game  ", "Test_ Game")]
    [InlineData("custom-game", null, "custom-game")]
    [InlineData("custom-game", "Unreal Engine", "custom-game")]
    [InlineData("custom-game", "Unreal Engine 5", "custom-game")]
    [InlineData("custom-game", "Unity Player", "custom-game")]
    [InlineData("custom-game", "Microsoft Windows", "custom-game")]
    [InlineData("custom-game", "  ", "custom-game")]
    public void Other_games_use_safe_product_or_process_names(string processName, string? productName, string expected)
        => Assert.Equal(expected, DisplayHelper.GameName(processName, () => productName));

    [Fact]
    public void An_installed_game_title_takes_precedence_over_engine_metadata_and_removes_trademarks()
        => Assert.Equal("Test_ Adventure", DisplayHelper.GameName("Game-Win64-Shipping", () => throw new InvalidOperationException("Metadata is not needed"), () => "Test: Adventure™®"));

    [Fact]
    public void A_known_game_or_launcher_does_not_need_access_to_installation_manifests()
    {
        Assert.Equal("Counter-Strike 2", DisplayHelper.GameName("cs2", () => null, () => throw new IOException("No access")));
        Assert.Null(DisplayHelper.GameName("steam", () => null, () => throw new IOException("No access")));
    }

    [Fact]
    public void Missing_installation_metadata_preserves_the_executable_product_name_fallback()
        => Assert.Equal("Test Game", DisplayHelper.GameName("Game", () => "Test Game", () => null));

    [Fact]
    public void SanitizeFileName_replaces_all_invalid_characters_without_truncating_the_extension()
    {
        var input = new string('a', 100) + new string(Path.GetInvalidFileNameChars()) + "_cut_0-10.mp4";

        var name = DisplayHelper.SanitizeFileName(input);

        Assert.Equal(new string('a', 100) + new string('_', Path.GetInvalidFileNameChars().Length) + "_cut_0-10.mp4", name);
        Assert.Equal(".mp4", Path.GetExtension(name));
    }

    [Theory]
    [InlineData("Zażółć gęślą_cut_0-10_25MB.mp4")]
    [InlineData("clip,with & punctuation.mp4")]
    public void SanitizeFileName_preserves_valid_names(string name)
        => Assert.Equal(name, DisplayHelper.SanitizeFileName(name));

    [Theory]
    [InlineData("Counter-Strike 2", "Counter-Strike 2")]
    [InlineData("  Elden Ring  ", "Elden Ring")]
    [InlineData("Half:Life <2>", "Half_Life _2_")]
    [InlineData("Gra/Mod\\Wersja?", "Gra_Mod_Wersja_")]
    [InlineData("Nazwa...", "Nazwa")]
    [InlineData("", "Pulpit")]
    [InlineData("   ", "Pulpit")]
    [InlineData(".", "Pulpit")]
    [InlineData("..", "Pulpit")]
    [InlineData("CON", "_CON")]
    [InlineData("aux", "_aux")]
    [InlineData("com1.exe", "_com1.exe")]
    [InlineData("console", "console")]
    public void SafeFolderName_produces_a_valid_windows_folder_name(string input, string expected)
        => Assert.Equal(expected, DisplayHelper.SafeFolderName(input));

    [Fact]
    public void SafeFolderName_cuts_long_names_to_80_characters()
    {
        var name = DisplayHelper.SafeFolderName(new string('a', 79) + ". " + new string('b', 30));

        Assert.True(name.Length <= 80);
        Assert.Equal(new string('a', 79), name);
    }

    [Fact]
    public void SafeFolderName_never_returns_invalid_characters()
    {
        var name = DisplayHelper.SafeFolderName(new string(Path.GetInvalidFileNameChars()) + "x");

        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));
        Assert.EndsWith("x", name);
    }
}
