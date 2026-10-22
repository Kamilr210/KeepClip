namespace KeepClip.Tests.Data;

public class TranscriptIntegrityTests
{
    public TranscriptIntegrityTests() => TestDb.Reset();

    [Fact]
    public void Failed_transcript_replacement_rolls_back_rows_and_the_search_index()
    {
        var clip = TestDb.AddClip();
        var segments = new SegmentRepository();
        segments.Insert(clip, 0, 1, "oryginalny");

        IEnumerable<(double, double, string)> InterruptedTranscription()
        {
            yield return (1, 2, "niekompletny");
            throw new InvalidOperationException("Testowy błąd transkrypcji");
        }

        Assert.Throws<InvalidOperationException>(() => segments.ReplaceAll(clip, InterruptedTranscription()));

        Assert.Equal("oryginalny", Assert.Single(segments.ListByClipId(clip))["text"]);
        Assert.Single(segments.Search("\"oryginalny\"*", null, 10));
        Assert.Empty(segments.Search("\"niekompletny\"*", null, 10));
    }

    [Fact]
    public void Transcript_text_is_stored_as_data_even_when_it_contains_sql_syntax()
    {
        var clip = TestDb.AddClip();
        var segments = new SegmentRepository();
        var text = "'); DELETE FROM clips; -- <script>alert('test')</script>";

        segments.ReplaceAll(clip, new[] { (0.0, 1.0, text) });

        Assert.Equal(text, Assert.Single(segments.ListByClipId(clip))["text"]);
        Assert.NotNull(new ClipRepository().GetById(clip));
        Assert.Single(segments.Search("\"delete\"*", null, 10));
    }
}