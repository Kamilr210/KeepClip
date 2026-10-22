using Whisper.net;

namespace KeepClip.Tests.Infrastructure;

public class TranscriptSegmentationTests
{
    private static WhisperToken Token(long start, long end, string text) => new() { Start = start, End = end, Text = text };
    private static SegmentData Segment(string text, params WhisperToken[] tokens) => new(text, TimeSpan.Zero, TimeSpan.FromSeconds(5), 0, 1, 1, 0, "pl", tokens);

    [Fact]
    public void Aligned_tokens_split_at_internal_silence_and_keep_word_spacing()
    {
        var output = new List<Transcriber.Segment>();
        var segment = Segment(" Hello world again", Token(0, 50, " Hello"), Token(50, 100, " world"), Token(350, 500, " again"));
        Transcriber.SplitOnInternalGaps(segment, 1.5, output);
        Assert.Equal(new[] { new Transcriber.Segment(0, 1, "Hello world"), new Transcriber.Segment(3.5, 5, "again") }, output);
    }

    [Theory]
    [InlineData(249, 1)]
    [InlineData(250, 2)]
    public void Gap_threshold_is_inclusive(long secondStart, int count)
    {
        var output = new List<Transcriber.Segment>();
        Transcriber.SplitOnInternalGaps(Segment("one two", Token(0, 100, "one"), Token(secondStart, 500, " two")), 1.5, output);
        Assert.Equal(count, output.Count);
        Assert.Equal("one two", string.Join(" ", output.Select(s => s.Text)));
    }

    [Fact]
    public void Special_and_empty_tokens_do_not_become_transcript_text()
    {
        var output = new List<Transcriber.Segment>();
        Transcriber.SplitOnInternalGaps(Segment("text", Token(0, 0, "[_start]"), Token(0, 0, ""), Token(0, 500, " text ")), 1.5, output);
        Assert.Equal(new Transcriber.Segment(0, 5, "text"), Assert.Single(output));
    }

    [Theory]
    [InlineData(200, 500)]
    [InlineData(0, 300)]
    public void Unaligned_tokens_fall_back_to_the_original_segment(long start, long end)
    {
        var output = new List<Transcriber.Segment>();
        Transcriber.SplitOnInternalGaps(Segment(" full text ", Token(start, end, "incomplete")), 1.5, output);
        Assert.Equal(new Transcriber.Segment(0, 5, "full text"), Assert.Single(output));
    }

    [Theory]
    [InlineData(" words ", 1)]
    [InlineData("   ", 0)]
    public void Segment_without_tokens_uses_nonempty_original_text(string text, int count)
    {
        var output = new List<Transcriber.Segment>();
        Transcriber.SplitOnInternalGaps(Segment(text), 1.5, output);
        Assert.Equal(count, output.Count);
        if (count > 0) Assert.Equal("words", output[0].Text);
    }

    [Fact]
    public void Whitespace_only_chunks_are_omitted()
    {
        var output = new List<Transcriber.Segment>();
        Transcriber.SplitOnInternalGaps(Segment("", Token(0, 100, "  "), Token(350, 500, " final ")), 1.5, output);
        Assert.Equal(new Transcriber.Segment(3.5, 5, "final"), Assert.Single(output));
    }
}