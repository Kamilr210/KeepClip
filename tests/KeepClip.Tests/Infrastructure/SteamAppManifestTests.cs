namespace KeepClip.Tests.Infrastructure;

public class SteamAppManifestTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\"Other\" { \"name\" \"Wrong\" \"installdir\" \"Game\" \"StateFlags\" \"4\" }")]
    [InlineData("\"AppState\" { \"name\" \"Game\" \"installdir\" \"Game\" \"StateFlags\" \"4\"")]
    [InlineData("\"AppState\" { \"name\" \"Game\" \"installdir\" \"Game\" \"StateFlags\" \"4\" } trailing")]
    [InlineData("\"AppState\" { \"name\" \"Game\" \"installdir\" \"Game\" \"StateFlags\" \"2\" }")]
    [InlineData("\"AppState\" { \"name\" \"Game\" \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"name\" \"Game\" \"name\" \"Other\" \"installdir\" \"Game\" \"StateFlags\" \"4\" }")]
    [InlineData("\"AppState\" { \"UserConfig\" { \"name\" \"Wrong\" } \"installdir\" \"Game\" \"StateFlags\" \"4\" }")]
    [InlineData("\"AppState\" { \"name\" \"unterminated }")]
    public void Incomplete_or_ambiguous_manifests_do_not_produce_a_game(string text)
        => Assert.Null(SteamAppManifest.Parse(text));

    [Fact]
    public void Excessive_nesting_is_rejected_without_overflowing_the_stack()
    {
        var text = "\"AppState\" { " + string.Concat(Enumerable.Repeat("\"child\" { ", 100)) + new string('}', 101);
        Assert.Null(SteamAppManifest.Parse(text));
    }
}