namespace KeepClip.Tests.Services;

public class ClipServiceTests
{
    private readonly string _root = TestEnvironment.NewTempDir("clip-service");
    private readonly ClipRepository _clips = new();
    private readonly SegmentRepository _segments = new();
    private readonly FakeClipOperations _operations = new();
    private readonly ClipService _service;

    public ClipServiceTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        Settings.SetClipsRoot(_root);
        _service = new ClipService(_clips, _segments, new PlaybackProxyService(_operations), _operations);
    }

    private long LocalClip(string name = "original.mp4")
    {
        File.WriteAllText(Path.Combine(_root, name), "original data");
        return TestDb.AddClip(name: name, dir: _root);
    }

    [Fact]
    public void Fix_replaces_the_file_preserves_timestamp_and_shifts_the_transcript()
    {
        var id = LocalClip();
        var original = _clips.GetById(id)!.Filepath!;
        File.SetLastWriteTimeUtc(original, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var timestamp = File.GetLastWriteTimeUtc(original);
        _segments.Insert(id, 0, 1, "drop");
        _segments.Insert(id, 1, 4, "keep");
        var fixedPath = Path.Combine(_root, "fixed.mp4");
        File.WriteAllText(fixedPath, "repaired data, longer");
        _operations.Fix = path => { Assert.Equal(original, path); return (true, fixedPath, "repaired", 2); };
        _operations.SendToTrash = File.Delete;
        _operations.Duration = _ => 12.5;
        _operations.Thumbnail = (clip, _) => { Assert.Equal(id, clip); return true; };

        var result = _service.Fix(id);

        Assert.True(result.IsSuccess);
        Assert.Equal("repaired data, longer", File.ReadAllText(original));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(original));
        Assert.False(File.Exists(fixedPath));
        Assert.Equal(12.5, _clips.GetById(id)!.Duration);
        Assert.Equal(1, _clips.GetById(id)!.HasThumb);
        var segment = Assert.Single(_segments.ListByClipId(id));
        Assert.Equal("keep", segment["text"]);
        Assert.Equal(0.0, segment["start_s"]);
        Assert.Equal(2.0, segment["end_s"]);
        Assert.Equal(1, result.Value!["segments_dropped"]);
    }

    [Fact]
    public void Failed_repair_leaves_the_original_and_transcript_intact()
    {
        var id = LocalClip();
        _segments.Insert(id, 0, 1, "original transcript");
        _operations.Fix = _ => (false, null, "encoder failed", 0);

        var result = _service.Fix(id);

        Assert.Equal(422, result.Code);
        Assert.Equal("original data", File.ReadAllText(_clips.GetById(id)!.Filepath!));
        Assert.Equal("original transcript", Assert.Single(_segments.ListByClipId(id))["text"]);
    }

    [Fact]
    public void Failure_to_recycle_the_original_discards_only_the_temporary_repair()
    {
        var id = LocalClip();
        var fixedPath = Path.Combine(_root, "fixed.mp4");
        File.WriteAllText(fixedPath, "repair");
        _operations.Fix = _ => (true, fixedPath, "ok", 0);
        _operations.SendToTrash = _ => throw new IOException("locked");

        var result = _service.Fix(id);

        Assert.Equal(500, result.Code);
        Assert.False(File.Exists(fixedPath));
        Assert.Equal("original data", File.ReadAllText(_clips.GetById(id)!.Filepath!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cut_keeps_existing_files_and_registers_only_outputs_inside_the_library(bool insideLibrary)
    {
        var id = LocalClip();
        var cuts = insideLibrary ? Path.Combine(_root, "Wycinki") : TestEnvironment.NewTempDir("external-cut");
        Directory.CreateDirectory(cuts);
        Settings.SetCutsRoot(cuts);
        var collision = Path.Combine(cuts, "original_cut_1-4_25MB.mp4");
        File.WriteAllText(collision, "existing output");
        var stages = new List<string>();
        _operations.Cut = (source, output, start, end, size, progress) =>
        {
            Assert.Equal(_clips.GetById(id)!.Filepath, source);
            Assert.Equal(1, start);
            Assert.Equal(4, end);
            Assert.Equal(25, size);
            Assert.EndsWith("original_cut_1-4_25MB_2.mp4", output);
            File.WriteAllText(output, "new output");
            progress?.Invoke(0.5, "cutting");
            return (true, "ok", new() { ["duration_s"] = 3.0 });
        };

        var result = _service.Cut(id, 1, 4, 25, (_, stage) => stages.Add(stage));

        Assert.True(result.IsSuccess);
        Assert.Equal(insideLibrary, result.Value!["in_library"]);
        Assert.Equal(insideLibrary ? 2 : 1, _clips.Count());
        Assert.Equal("existing output", File.ReadAllText(collision));
        Assert.Equal("new output", File.ReadAllText((string)result.Value["output_path"]!));
        Assert.Equal(new[] { "preparing", "cutting", "saving" }, stages);
    }

    [Fact]
    public void Failed_cut_does_not_add_a_clip()
    {
        var id = LocalClip();
        _operations.Cut = (_, _, _, _, _, _) => (false, "encoder failed", new());
        Assert.Equal(422, _service.Cut(id, 0, 3, null).Code);
        Assert.Equal(1, _clips.Count());
    }

    [Theory]
    [InlineData(-1, 1, null)]
    [InlineData(0, 0.1, null)]
    [InlineData(0, 1801, null)]
    [InlineData(double.NaN, 1, null)]
    [InlineData(0, double.PositiveInfinity, null)]
    [InlineData(0, 1, double.NaN)]
    [InlineData(0, 1, -1.0)]
    public void Invalid_cut_parameters_are_rejected_before_starting_an_encoder(double start, double end, double? size)
    {
        var id = LocalClip();
        Assert.Equal(422, _service.Cut(id, start, end, size).Code);
        Assert.Equal(1, _clips.Count());
        Assert.False(Directory.Exists(Settings.GetCutsRoot()));
    }

    [Fact]
    public async Task Delete_without_file_removal_preserves_local_data_and_cascades_database_links()
    {
        var id = LocalClip();
        var path = _clips.GetById(id)!.Filepath!;
        _segments.Insert(id, 0, 1, "transcript");
        TestDb.Exec("INSERT INTO folders(name) VALUES('test'); INSERT INTO folder_clips(folder_id, clip_id) VALUES(last_insert_rowid(), $id)", ("$id", id));
        Directory.CreateDirectory(Config.ThumbsDir);
        File.WriteAllText(Config.ThumbPath(id), "thumbnail");

        var result = await _service.DeleteAsync(id, false);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Config.ThumbPath(id)));
        Assert.Null(_clips.GetById(id));
        Assert.Empty(_segments.ListByClipId(id));
        Assert.Equal(0, TestDb.ScalarLong("SELECT count(*) FROM folder_clips"));
    }

    [Fact]
    public async Task Failed_local_deletion_preserves_the_library_entry_and_transcript()
    {
        var id = LocalClip();
        _segments.Insert(id, 0, 1, "keep");
        _operations.SendToTrashWithRetry = _ => "file is locked";

        var result = await _service.DeleteAsync(id, true);

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.Code);
        Assert.NotNull(_clips.GetById(id));
        Assert.Single(_segments.ListByClipId(id));
        Assert.True(File.Exists(_clips.GetById(id)!.Filepath));
    }

    [Fact]
    public async Task Successful_local_deletion_reports_recycling()
    {
        var id = LocalClip();
        _operations.SendToTrashWithRetry = path => { File.Delete(path); return null; };
        var result = await _service.DeleteAsync(id, true);
        Assert.True(result.IsSuccess);
        Assert.Equal(true, result.Value!["file_sent_to_trash"]);
        Assert.Null(_clips.GetById(id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cloud_deletion_removes_the_database_entry_only_after_remote_success(bool success)
    {
        var id = TestDb.AddClip(dir: _root);
        TestDb.Exec("UPDATE clips SET storage='cloud', remote_id='test-remote' WHERE id=$id", ("$id", id));
        _operations.RemoteTrash = remote => { Assert.Equal("test-remote", remote); return success ? Task.CompletedTask : Task.FromException(new IOException("offline")); };

        var result = await _service.DeleteAsync(id, true);

        Assert.Equal(success, result.IsSuccess);
        Assert.Equal(success, _clips.GetById(id) is null);
        if (success) Assert.Equal(true, result.Value!["cloud_deleted"]);
        else Assert.Equal(502, result.Code);
    }

    [Fact]
    public async Task Cloud_deletion_requires_a_remote_id()
    {
        var id = TestDb.AddClip(dir: _root);
        TestDb.Exec("UPDATE clips SET storage='cloud' WHERE id=$id", ("$id", id));
        Assert.Equal(409, (await _service.DeleteAsync(id, true)).Code);
        Assert.NotNull(_clips.GetById(id));
    }

    [Fact]
    public void Retranscribe_replaces_the_transcript_and_search_index_after_success()
    {
        var id = LocalClip();
        _segments.Insert(id, 0, 1, "stary");
        Settings.SetLanguage("pl");
        _operations.Transcription = (_, progress) => { progress?.Invoke(0.5, "transcribing"); return new() { new(1, 2, "nowy") }; };

        var result = _service.Retranscribe(id);

        Assert.True(result.IsSuccess);
        Assert.Equal("nowy", Assert.Single(_segments.ListByClipId(id))["text"]);
        Assert.Empty(_segments.Search("\"stary\"*", null, 10));
        Assert.Single(_segments.Search("\"nowy\"*", null, 10));
        Assert.NotNull(_clips.GetById(id)!.TranscribedAt);
    }

    [Fact]
    public void Failed_transcription_preserves_the_previous_transcript()
    {
        var id = LocalClip();
        _segments.Insert(id, 0, 1, "keep");
        _operations.Transcription = (_, _) => throw new IOException("decode failed");
        Assert.Equal(500, _service.Retranscribe(id).Code);
        Assert.Equal("keep", Assert.Single(_segments.ListByClipId(id))["text"]);
        Assert.Null(_clips.GetById(id)!.TranscribedAt);
    }
}