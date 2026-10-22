using System.Net;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class SearchSecurityApiTests
{
    private readonly HttpClient _http;

    public SearchSecurityApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Theory]
    [InlineData(513)]
    [InlineData(4096)]
    public async Task Search_rejects_oversized_queries_without_changing_the_library(int length)
    {
        var clip = TestDb.AddClip();
        new SegmentRepository().Insert(clip, 0, 1, "headshot");

        var response = await _http.GetAsync("/api/search?q=" + new string('a', length));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("searchQueryTooComplex", (string)(await response.Json())["error"]!);
        Assert.Single(new SegmentRepository().ListByClipId(clip));
        Assert.Single((await _http.GetJson("/api/search?q=head"))["results"]!.AsArray());
    }

    [Fact]
    public async Task Search_rejects_too_many_terms_before_running_fts()
    {
        var query = string.Join(" ", Enumerable.Repeat("a", 33));

        var response = await _http.GetAsync("/api/search?q=" + Uri.EscapeDataString(query));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("searchQueryTooComplex", (string)(await response.Json())["error"]!);
    }

    [Fact]
    public async Task Search_accepts_the_length_and_term_limits()
    {
        var clip = TestDb.AddClip();
        var word = new string('a', 512);
        new SegmentRepository().Insert(clip, 0, 1, word + " headshot");

        var byLength = await _http.GetJson("/api/search?q=" + word);
        var byTerms = await _http.GetJson("/api/search?q=" + Uri.EscapeDataString(string.Join(" ", Enumerable.Repeat("head", 32))));

        Assert.Equal(clip, (long)Assert.Single(byLength["results"]!.AsArray())!["clip_id"]!);
        Assert.Equal(clip, (long)Assert.Single(byTerms["results"]!.AsArray())!["clip_id"]!);
    }

    [Theory]
    [InlineData("\" OR * NEAR() NOT - : { }", "OR NEAR NOT")]
    [InlineData("'; DROP TABLE clips; --", "DROP TABLE clips")]
    [InlineData("zażółć gęślą", "Zażółć gęślą jaźń")]
    public async Task Search_treats_input_as_words_instead_of_fts_or_sql_syntax(string query, string transcript)
    {
        var clip = TestDb.AddClip();
        new SegmentRepository().Insert(clip, 0, 1, transcript);

        var result = await _http.GetJson("/api/search?q=" + Uri.EscapeDataString(query) + "&sort=" + Uri.EscapeDataString("mtime; DROP TABLE clips; --"));

        Assert.Equal(clip, (long)Assert.Single(result["results"]!.AsArray())!["clip_id"]!);
        Assert.NotNull(new ClipRepository().GetById(clip));
        Assert.Equal(transcript, Assert.Single(new SegmentRepository().ListByClipId(clip))["text"]);
    }
}