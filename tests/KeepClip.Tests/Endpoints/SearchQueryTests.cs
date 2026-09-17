namespace KeepClip.Tests.Endpoints;

public class SearchQueryTests
{
    [Theory]
    [InlineData("Hello World", "\"hello\"* AND \"world\"*")]
    [InlineData("  jedno  ", "\"jedno\"*")]
    [InlineData("Zażółć Gęślą", "\"zażółć\"* AND \"gęślą\"*")]
    [InlineData("co-op mode!", "\"co\"* AND \"op\"* AND \"mode\"*")]
    [InlineData("\"cudzysłów\" (nawias)", "\"cudzysłów\"* AND \"nawias\"*")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("!!! ???", "")]
    public void ToFtsQuery_turns_words_into_prefix_tokens_joined_with_AND(string input, string expected)
        => Assert.Equal(expected, SearchEndpoints.ToFtsQuery(input));

    [Fact]
    public void ToFtsQuery_never_lets_fts_operators_through()
    {
        var q = SearchEndpoints.ToFtsQuery("a OR b NOT c NEAR(d)");

        Assert.Equal("\"a\"* AND \"or\"* AND \"b\"* AND \"not\"* AND \"c\"* AND \"near\"* AND \"d\"*", q);
    }
}
