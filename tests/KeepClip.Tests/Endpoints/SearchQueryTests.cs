namespace KeepClip.Tests.Endpoints;

public class SearchQueryTests
{
    [Theory]
    [InlineData("Hello World", "\"hello\"* AND \"world\"*")]
    [InlineData("  jedno  ", "\"jedno\"*")]
    [InlineData("ZaЕјГіЕ‚Д‡ GД™Е›lД…", "\"zaЕјГіЕ‚Д‡\"* AND \"gД™Е›lД…\"*")]
    [InlineData("co-op mode!", "\"co\"* AND \"op\"* AND \"mode\"*")]
    [InlineData("\"cudzysЕ‚Гіw\" (nawias)", "\"cudzysЕ‚Гіw\"* AND \"nawias\"*")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!! ???", "")]
    public void ToFtsQuery_turns_words_into_prefix_tokens_joined_with_AND(string input, string expected)
        => Assert.Equal(expected, SearchService.ToFtsQuery(input));

    [Fact]
    public void ToFtsQuery_never_lets_fts_operators_through()
    {
        var q = SearchService.ToFtsQuery("a OR b NOT c NEAR(d)");

        Assert.Equal("\"a\"* AND \"or\"* AND \"b\"* AND \"not\"* AND \"c\"* AND \"near\"* AND \"d\"*", q);
    }
}
