using Microsoft.Data.Sqlite;

namespace KeepClip.Tests.Workers;

public class ScannerConsistencyTests
{
    private readonly string _root = TestEnvironment.NewTempDir("scanner-consistency");
    private readonly ClipRepository _clips = new();

    public ScannerConsistencyTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        Settings.SetClipsRoot(_root);
    }

    private string Video(string relativePath, int size = 8)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        return path;
    }

    private long OnlyClipId() => Convert.ToInt64(Assert.Single(_clips.List(null, null, 100, false))["id"]);

    private static string Thumbnail(long id)
    {
        Directory.CreateDirectory(Config.ThumbsDir);
        var path = Config.ThumbPath(id);
        File.WriteAllText(path, "synthetic thumbnail");
        TestDb.Exec("UPDATE clips SET has_thumb=1 WHERE id=$id", ("$id", id));
        return path;
    }

    [Fact]
    public void Changed_timestamp_invalidates_thumbnail_even_when_size_is_unchanged()
    {
        var path = Video("clip.mp4");
        Scanner.Scan();
        var id = OnlyClipId();
        var thumbnail = Thumbnail(id);
        var modified = File.GetLastWriteTimeUtc(path).AddSeconds(10);
        File.SetLastWriteTimeUtc(path, modified);

        var result = Scanner.Scan();

        Assert.Equal(1, result["updated"]);
        Assert.Equal(0, result["added"]);
        Assert.Equal(0, result["removed"]);
        Assert.Equal(id, OnlyClipId());
        var clip = _clips.GetById(id)!;
        Assert.Equal(8L, clip.SizeBytes);
        Assert.Equal(((DateTimeOffset)modified).ToUnixTimeMilliseconds() / 1000.0, clip.Mtime);
        Assert.Equal(0, clip.HasThumb);
        Assert.False(File.Exists(thumbnail));
    }

    [Fact]
    public void File_still_being_written_keeps_its_existing_metadata_until_it_settles()
    {
        var path = Video("recording.mp4");
        Scanner.Scan();
        var id = OnlyClipId();
        var thumbnail = Thumbnail(id);
        File.WriteAllBytes(path, new byte[64]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var pending = Scanner.Scan();

            Assert.False(pending.ContainsKey("error"));
            Assert.Equal(1, pending["found"]);
            Assert.Equal(0, pending["updated"]);
            Assert.Equal(0, pending["removed"]);
            Assert.Equal(8L, _clips.GetById(id)!.SizeBytes);
            Assert.True(File.Exists(thumbnail));
        }

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(1, Scanner.Scan()["updated"]);
        Assert.Equal(id, OnlyClipId());
        Assert.Equal(64L, _clips.GetById(id)!.SizeBytes);
        Assert.False(File.Exists(thumbnail));
    }

    [Fact]
    public void Moving_a_file_reindexes_its_path_and_removes_stale_associations()
    {
        var path = Video(Path.Combine("Old game", "clip.mp4"));
        Scanner.Scan();
        var oldId = OnlyClipId();
        var thumbnail = Thumbnail(oldId);
        var segments = new SegmentRepository();
        segments.Insert(oldId, 0, 1, "old transcript");
        var folders = new FolderRepository();
        var folderId = Convert.ToInt64(folders.Create("Saved")!["id"]);
        Assert.Equal(AddClipResult.Ok, folders.AddClip(folderId, oldId));
        var destination = Path.Combine(_root, "New game", "renamed.MP4");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(path, destination);

        var result = Scanner.Scan();

        Assert.Equal(1, result["found"]);
        Assert.Equal(1, result["added"]);
        Assert.Equal(1, result["removed"]);
        Assert.Null(_clips.GetById(oldId));
        var clip = _clips.GetById(OnlyClipId())!;
        Assert.Equal(destination, clip.Filepath);
        Assert.Equal("renamed.MP4", clip.Filename);
        Assert.Equal("New game", clip.Game);
        Assert.Empty(segments.ListByClipId(oldId));
        Assert.Empty(segments.Search("\"transcript\"*", null, 10));
        Assert.Empty(folders.ListClips(folderId, null, 10)!.Value.clips);
        Assert.False(File.Exists(thumbnail));
        Assert.Equal(new byte[8], File.ReadAllBytes(destination));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("file")]
    [InlineData("invalid")]
    public void Invalid_root_never_discards_existing_library_data(string kind)
    {
        Video("preserved.mp4");
        Scanner.Scan();
        var id = OnlyClipId();
        var thumbnail = Thumbnail(id);
        new SegmentRepository().Insert(id, 0, 1, "preserved");
        var root = kind switch
        {
            "file" => Video("not-a-directory.txt"),
            "invalid" => Path.Combine(_root, "invalid\0path"),
            _ => Path.Combine(_root, "missing"),
        };
        Settings.SetClipsRoot(root);

        var result = Scanner.Scan();

        Assert.IsType<string>(result["error"]);
        Assert.Equal(0, result["removed"]);
        Assert.Equal(id, OnlyClipId());
        Assert.Equal(1, _clips.GetById(id)!.HasThumb);
        Assert.Equal("preserved", Assert.Single(new SegmentRepository().ListByClipId(id))["text"]);
        Assert.True(File.Exists(thumbnail));
    }

    [Fact]
    public async Task Concurrent_scans_add_each_file_once_and_leave_the_scanner_reusable()
    {
        for (int i = 0; i < 16; i++) Video(Path.Combine("Game", $"clip-{i}.mp4"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return Scanner.Scan();
        })).ToArray();

        start.SetResult();
        var results = await Task.WhenAll(scans).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.All(results, result => Assert.False(result.ContainsKey("error")));
        Assert.Equal(16, results.Sum(result => Convert.ToInt32(result["added"])));
        Assert.All(results, result => Assert.Equal(16, result["found"]));
        Assert.Equal(16, _clips.Count());
        Assert.Equal(16L, TestDb.ScalarLong("SELECT COUNT(DISTINCT filepath) FROM clips"));
        Assert.Equal(0, Scanner.Scan()["added"]);
    }

    [Fact]
    public void Failed_database_write_rolls_back_records_and_keeps_their_thumbnails()
    {
        var original = Video("original.mp4");
        Scanner.Scan();
        var id = OnlyClipId();
        var thumbnail = Thumbnail(id);
        new SegmentRepository().Insert(id, 0, 1, "keep transcript");
        File.Delete(original);
        Video("incoming.mp4");
        TestDb.Exec("CREATE TRIGGER reject_scanner_insert BEFORE INSERT ON clips WHEN NEW.filename='incoming.mp4' BEGIN SELECT RAISE(ABORT, 'synthetic scan failure'); END");
        try
        {
            var error = Assert.Throws<SqliteException>(() => Scanner.Scan());

            Assert.Contains("synthetic scan failure", error.Message);
            Assert.Equal(id, OnlyClipId());
            Assert.Equal(1, _clips.GetById(id)!.HasThumb);
            Assert.Equal("keep transcript", Assert.Single(new SegmentRepository().ListByClipId(id))["text"]);
            Assert.Equal("synthetic thumbnail", File.ReadAllText(thumbnail));
        }
        finally { TestDb.Exec("DROP TRIGGER reject_scanner_insert"); }

        var retry = Scanner.Scan();
        Assert.Equal(1, retry["added"]);
        Assert.Equal(1, retry["removed"]);
        Assert.False(File.Exists(thumbnail));
        Assert.Null(_clips.GetById(id));
    }
}