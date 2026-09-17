namespace KeepClip.Tests.Infrastructure;

public class DisplayHelperTests
{
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
