using System.Text.Json.Nodes;

namespace KeepClip.Tests.Services;

public class SettingsTests
{
    public SettingsTests() => TestSettings.Reset();

    [Fact]
    public void Fresh_install_starts_in_english_and_unconfigured()
    {
        Assert.Equal(Config.DefaultLanguage, Settings.GetLanguage());
        Assert.False(Settings.IsConfigured());
        Assert.Equal(Config.DefaultClipsRoot, Settings.GetClipsRoot());
        Assert.Null(Settings.GetString("accent_theme"));
        Assert.Empty(Settings.GetObject("onboarding"));
    }

    [Fact]
    public void SetClipsRoot_marks_installation_as_configured()
    {
        Settings.SetClipsRoot(@"C:\Klipy");

        Assert.True(Settings.IsConfigured());
        Assert.Equal(@"C:\Klipy", Settings.GetClipsRoot());
    }

    [Fact]
    public void Cuts_folder_defaults_to_subfolder_of_clips_root()
    {
        Settings.SetClipsRoot(@"C:\Klipy");
        Assert.Equal(Path.Combine(@"C:\Klipy", Config.CutsSubdir), Settings.GetCutsRoot());

        Settings.SetCutsRoot(@"D:\Wycinki");
        Assert.Equal(@"D:\Wycinki", Settings.GetCutsRoot());
    }

    [Theory]
    [InlineData("pl", "pl")]
    [InlineData("uk", "uk")]
    [InlineData("xx", "en")]
    public void GetLanguage_accepts_only_supported_codes(string stored, string expected)
    {
        File.WriteAllText(TestSettings.Path, $$"""{"ui_language": "{{stored}}"}""");

        Assert.Equal(expected, Settings.GetLanguage());
    }

    [Fact]
    public void Corrupt_settings_file_falls_back_to_defaults_without_throwing()
    {
        File.WriteAllText(TestSettings.Path, "{ to nie jest json");

        Assert.Equal(Config.DefaultLanguage, Settings.GetLanguage());
        Assert.False(Settings.IsConfigured());

        Settings.SetLanguage("pl");
        Assert.Equal("pl", Settings.GetLanguage());
    }

    [Fact]
    public void Objects_round_trip_and_copies_are_independent()
    {
        Settings.SetObject("onboarding", new JsonObject { ["started"] = true, ["list"] = new JsonArray("a") });

        var read = Settings.GetObject("onboarding");
        Assert.True((bool)read["started"]!);
        read["started"] = false;

        Assert.True((bool)Settings.GetObject("onboarding")["started"]!);
    }

    [Fact]
    public void Writes_keep_unrelated_keys()
    {
        Settings.SetClipsRoot(@"C:\Klipy");
        Settings.SetString("accent_theme", "green");
        Settings.SetLanguage("ru");

        Assert.Equal(@"C:\Klipy", Settings.GetClipsRoot());
        Assert.Equal("green", Settings.GetString("accent_theme"));
        Assert.Equal("ru", Settings.GetLanguage());
    }
}
