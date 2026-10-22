namespace KeepClip.Tests.Infrastructure;

public class DisplayHelperTests
{
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
