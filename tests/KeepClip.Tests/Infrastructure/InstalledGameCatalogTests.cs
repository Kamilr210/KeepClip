using System.Text;
using System.Text.Json;

namespace KeepClip.Tests.Infrastructure;

public class InstalledGameCatalogTests
{
    private sealed class Clock : TimeProvider
    {
        internal long Seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }

    private readonly string _root = TestEnvironment.NewTempDir("game-installs");
    private string EpicManifests => Path.Combine(_root, "epic-manifests");

    private string SteamGame(string library, string directory, string name, int id = 1)
    {
        var steamapps = Directory.CreateDirectory(Path.Combine(_root, library, "steamapps")).FullName;
        var escaped = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{id}.acf"), $"\"AppState\" {{ \"name\" \"{escaped}\" \"installdir\" \"{directory}\" \"StateFlags\" \"4\" }}");
        var executable = Path.Combine(steamapps, "common", directory, "Binaries", "Win64", "Game-Win64-Shipping.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "synthetic executable path");
        return executable;
    }

    private string EpicGame(string directory, string name, string app = "game", bool incomplete = false, string? parent = null)
    {
        Directory.CreateDirectory(EpicManifests);
        File.WriteAllText(Path.Combine(EpicManifests, app + ".item"), JsonSerializer.Serialize(new { AppName = app, MainGameAppName = parent ?? app, InstallLocation = directory, DisplayName = name, bIsIncompleteInstall = incomplete }));
        return Path.Combine(directory, "Binaries", "Win64", "Game-Win64-Shipping.exe");
    }

    [Fact]
    public void Steam_uses_the_title_of_the_matching_installation_on_each_library_drive()
    {
        var first = SteamGame("library-one", "InternalCodeName", "The First Game");
        var second = SteamGame("library-two", "InternalCodeName", "Druga gra: Zażółć");
        var catalog = new InstalledGameCatalog(EpicManifests);

        Assert.Equal("The First Game", catalog.Find(first));
        Assert.Equal("Druga gra: Zażółć", catalog.Find(second));
        Assert.Null(catalog.Find(first.Replace("InternalCodeName", "InternalCodeName-copy")));
        Assert.Equal("Druga gra_ Zażółć", DisplayHelper.GameName("Game-Win64-Shipping", () => "Unreal Engine", () => catalog.Find(second)));
    }

    [Fact]
    public void Steam_handles_utf8_bom_comments_nested_values_and_escaped_titles()
    {
        var executable = SteamGame("library", "TestGame", "ignored");
        var manifest = Path.Combine(_root, "library", "steamapps", "appmanifest_1.acf");
        File.WriteAllText(manifest, """
            // install metadata
            "AppState"
            {
                "UserConfig" { "name" "Not the game title" }
                "name" "A \"Quoted\" Game {Edition}"
                "installdir" "TestGame"
                "StateFlags" "68"
            }
            """, new UTF8Encoding(true));

        Assert.Equal("A \"Quoted\" Game {Edition}", new InstalledGameCatalog(EpicManifests).Find(executable));
    }

    [Fact]
    public void Epic_matches_whole_directory_boundaries_and_the_most_specific_installation()
    {
        var directory = Path.Combine(_root, "Epic Games", "Game");
        var parent = EpicGame(directory, "First Game", "first");
        var child = EpicGame(Path.Combine(directory, "Sequel"), "Second Game", "second");
        var catalog = new InstalledGameCatalog(EpicManifests);

        Assert.Equal("First Game", catalog.Find(parent));
        Assert.Equal("Second Game", catalog.Find(child.ToUpperInvariant()));
        Assert.Null(catalog.Find(Path.Combine(directory + "-copy", "Game.exe")));
        Assert.Null(catalog.Find(Path.Combine(directory, "..", "Unrelated", "Game.exe")));
    }

    [Fact]
    public void Epic_ignores_dlc_and_incomplete_installs_instead_of_overwriting_the_base_title()
    {
        var directory = Path.Combine(_root, "Epic Games", "Base");
        var executable = EpicGame(directory, "Base Game", "base");
        EpicGame(directory, "Expansion", "dlc", parent: "base");
        var unfinished = EpicGame(Path.Combine(_root, "unfinished"), "Not installed", "pending", incomplete: true);
        var catalog = new InstalledGameCatalog(EpicManifests);

        Assert.Equal("Base Game", catalog.Find(executable));
        Assert.Null(catalog.Find(unfinished));
    }

    [Fact]
    public void Conflicting_titles_for_the_same_installation_are_not_guessed()
    {
        var first = SteamGame("library", "Shared", "First Game", 1);
        SteamGame("library", "Shared", "Second Game", 2);
        var second = EpicGame(Path.Combine(_root, "Epic Games", "Shared"), "First Game", "first");
        EpicGame(Path.Combine(_root, "Epic Games", "Shared"), "Second Game", "second");
        var catalog = new InstalledGameCatalog(EpicManifests);

        Assert.Null(catalog.Find(first));
        Assert.Null(catalog.Find(second));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Catalog_refreshes_after_a_minute_instead_of_reading_manifests_on_every_poll(bool steam)
    {
        var clock = new Clock();
        var catalog = new InstalledGameCatalog(EpicManifests, clock);
        var path = steam ? SteamGame("library", "Game", "First title") : EpicGame(Path.Combine(_root, "Game"), "First title");
        Assert.Equal("First title", catalog.Find(path));

        if (steam) SteamGame("library", "Game", "Updated title");
        else EpicGame(Path.Combine(_root, "Game"), "Updated title");
        clock.Seconds = 59;
        Assert.Equal("First title", catalog.Find(path));
        clock.Seconds = 60;
        Assert.Equal("Updated title", catalog.Find(path));

        var manifest = steam ? Path.Combine(_root, "library", "steamapps", "appmanifest_1.acf") : Path.Combine(EpicManifests, "game.item");
        File.Delete(manifest);
        clock.Seconds = 120;
        Assert.Null(catalog.Find(path));
    }

    [Fact]
    public void Missing_manifests_are_retried_and_a_new_installation_is_discovered()
    {
        var clock = new Clock();
        var catalog = new InstalledGameCatalog(EpicManifests, clock);
        var path = Path.Combine(_root, "Game", "Binaries", "Win64", "Game-Win64-Shipping.exe");
        Assert.Null(catalog.Find(path));
        EpicGame(Path.Combine(_root, "Game"), "New game");

        clock.Seconds = 60;

        Assert.Equal("New game", catalog.Find(path));
    }

    [Fact]
    public void Broken_locked_or_oversized_manifests_do_not_hide_a_valid_game()
    {
        var executable = SteamGame("library", "Valid", "Valid Game", 2);
        var steamapps = Path.Combine(_root, "library", "steamapps");
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_1.acf"), "\"AppState\" { incomplete");
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_3.acf"), new string('x', 300_000));
        var locked = Path.Combine(steamapps, "appmanifest_4.acf");
        File.WriteAllText(locked, "locked");
        var epicExecutable = EpicGame(Path.Combine(_root, "Epic Game"), "Epic Game");
        File.WriteAllText(Path.Combine(EpicManifests, "broken.item"), "{");
        File.WriteAllText(Path.Combine(EpicManifests, "large.item"), new string('x', 300_000));
        using var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var catalog = new InstalledGameCatalog(EpicManifests);

        Assert.Equal("Valid Game", catalog.Find(executable));
        Assert.Equal("Epic Game", catalog.Find(epicExecutable));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"InstallLocation\":\"relative\",\"DisplayName\":\"Wrong\"}")]
    [InlineData("{\"InstallLocation\":\"C:\\\\\",\"DisplayName\":\"Wrong\"}")]
    [InlineData("{\"InstallLocation\":12,\"DisplayName\":\"Wrong\"}")]
    public void Invalid_epic_metadata_does_not_claim_an_unrelated_process(string json)
    {
        Directory.CreateDirectory(EpicManifests);
        File.WriteAllText(Path.Combine(EpicManifests, "invalid.item"), json);

        Assert.Null(new InstalledGameCatalog(EpicManifests).Find(Path.Combine(_root, "Other", "Game.exe")));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\Other")]
    [InlineData("C:\\Other")]
    public void Steam_installation_paths_cannot_escape_the_library(string directory)
    {
        var executable = SteamGame("library", "Valid", "Valid Game");
        var manifest = Path.Combine(_root, "library", "steamapps", "appmanifest_1.acf");
        var text = File.ReadAllText(manifest).Replace("\"installdir\" \"Valid\"", $"\"installdir\" \"{directory}\"");
        File.WriteAllText(manifest, text);

        Assert.Null(new InstalledGameCatalog(EpicManifests).Find(executable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Game.exe")]
    [InlineData("C:Game.exe")]
    public void An_unavailable_or_relative_executable_path_has_no_installed_title(string? path)
        => Assert.Null(new InstalledGameCatalog(EpicManifests).Find(path));
}