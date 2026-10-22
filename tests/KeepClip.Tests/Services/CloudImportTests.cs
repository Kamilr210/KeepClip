using System.Globalization;

namespace KeepClip.Tests.Services;

public class CloudImportTests
{
    private readonly string _root = TestEnvironment.NewTempDir("cloud-import");

    public CloudImportTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        Settings.SetClipsRoot(_root);
    }

    [Fact]
    public void Import_filters_non_video_files_and_preserves_metadata()
    {
        var file = Remote("remote-one", "a.MP4", game: "CS2", duration: "12.5", mtime: "1234.5");
        var result = CloudService.ImportClips(new[] { file, Remote("text", "a.txt") });

        Assert.Equal(new CloudService.SyncResult(1, 0, 1, 1), result);
        var row = Assert.Single(new ClipRepository().List(null, null, 10, false));
        Assert.Equal("cloud", row["storage"]);
        var clip = new ClipRepository().GetById(Convert.ToInt64(row["id"]))!;
        Assert.Equal("CS2", clip.Game);
        Assert.Equal("remote-one", clip.RemoteId);
        Assert.Equal(12.5, clip.Duration);
        Assert.Equal(1234.5, clip.Mtime);
        Assert.Equal(Path.Combine(_root, "CS2", "a.MP4"), clip.Filepath);
        Assert.False(File.Exists(clip.Filepath));
    }

    [Fact]
    public void Repeated_import_is_idempotent_and_retains_transcripts_and_favorites()
    {
        var file = Remote("same", "a.mp4");
        CloudService.ImportClips(new[] { file });
        var id = TestDb.ScalarLong("SELECT id FROM clips WHERE remote_id='same'");
        new ClipRepository().ToggleFavorite(id);
        new SegmentRepository().Insert(id, 0, 1, "zachowany tekst");

        var repeated = CloudService.ImportClips(new[] { file });

        Assert.Equal(new CloudService.SyncResult(0, 0, 1, 0), repeated);
        Assert.Equal(1, new ClipRepository().Count());
        Assert.Equal(1, new ClipRepository().GetById(id)!.Favorite);
        Assert.Equal("zachowany tekst", Assert.Single(new SegmentRepository().ListByClipId(id))["text"]);
    }

    [Fact]
    public void Changed_remote_metadata_updates_the_existing_clip_without_moving_its_path()
    {
        CloudService.ImportClips(new[] { Remote("same", "a.mp4", duration: "10") });
        var id = TestDb.ScalarLong("SELECT id FROM clips WHERE remote_id='same'");
        var oldPath = new ClipRepository().GetById(id)!.Filepath;

        var changed = Remote("same", "renamed.mp4", duration: "20", mtime: "5000") with { Size = 999 };
        var result = CloudService.ImportClips(new[] { changed });

        Assert.Equal(1, result.Updated);
        var clip = new ClipRepository().GetById(id)!;
        Assert.Equal("renamed.mp4", clip.Filename);
        Assert.Equal(999, clip.SizeBytes);
        Assert.Equal(5000, clip.Mtime);
        Assert.Equal(10, clip.Duration);
        Assert.Equal(oldPath, clip.Filepath);
    }

    [Fact]
    public void Legacy_cloud_entry_gets_its_remote_id_without_creating_a_duplicate()
    {
        var id = TestDb.AddClip(name: "legacy.mp4", dir: _root);
        TestDb.Exec("UPDATE clips SET storage='cloud', duration=0 WHERE id=$id", ("$id", id));

        var result = CloudService.ImportClips(new[] { Remote("resolved", "legacy.mp4", duration: "15") });

        Assert.Equal(new CloudService.SyncResult(0, 1, 1, 0), result);
        Assert.Equal(1, new ClipRepository().Count());
        Assert.Equal("resolved", new ClipRepository().GetById(id)!.RemoteId);
        Assert.Equal(15, new ClipRepository().GetById(id)!.Duration);
    }

    [Fact]
    public void Filename_collisions_preserve_existing_local_files_and_allocate_distinct_paths()
    {
        File.WriteAllText(Path.Combine(_root, "clip.mp4"), "original local data");
        File.WriteAllText(Path.Combine(_root, "clip_cloud_abcdefgh.mp4"), "another local file");

        CloudService.ImportClips(new[] { Remote("abcdefgh-one", "clip.mp4", game: "_root"), Remote("abcdefgh-two", "clip.mp4", game: "_root") });

        using var con = Db.Open();
        var paths = con.Query("SELECT filepath FROM clips ORDER BY id").Select(r => (string)r["filepath"]!).ToArray();
        Assert.Equal(new[] { Path.Combine(_root, "clip_cloud_abcdefgh_2.mp4"), Path.Combine(_root, "clip_cloud_abcdefgh_3.mp4") }, paths);
        Assert.Equal("original local data", File.ReadAllText(Path.Combine(_root, "clip.mp4")));
        Assert.Equal("another local file", File.ReadAllText(Path.Combine(_root, "clip_cloud_abcdefgh.mp4")));
    }

    [Fact]
    public void Long_remote_names_keep_the_video_extension_in_the_local_path()
    {
        CloudService.ImportClips(new[] { Remote("long", new string('a', 120) + ".mp4") });

        using var con = Db.Open();
        var path = (string)con.QueryOne("SELECT filepath FROM clips")!["filepath"]!;
        Assert.Equal(".mp4", Path.GetExtension(path));
        Assert.True(Path.GetFileName(path).Length <= 80);
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    [InlineData("1e300")]
    public void Invalid_numeric_metadata_uses_finite_fallbacks(string invalid)
    {
        var file = Remote("invalid", "a.mp4", duration: invalid, mtime: invalid);
        CloudService.ImportClips(new[] { file });

        var id = TestDb.ScalarLong("SELECT id FROM clips");
        var clip = new ClipRepository().GetById(id)!;
        Assert.Equal(0, clip.Duration);
        Assert.Equal(file.CreatedTime!.Value.ToUnixTimeMilliseconds() / 1000.0, clip.Mtime);
    }

    [Fact]
    public void Failure_mid_import_rolls_back_the_whole_batch()
    {
        TestDb.Exec("CREATE TRIGGER reject_test_import BEFORE INSERT ON clips WHEN NEW.filename='bad.mp4' BEGIN SELECT RAISE(ABORT, 'test failure'); END");
        try
        {
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => CloudService.ImportClips(new[] { Remote("good", "good.mp4"), Remote("bad", "bad.mp4") }));
            Assert.Equal(0, new ClipRepository().Count());
        }
        finally { TestDb.Exec("DROP TRIGGER reject_test_import"); }
    }

    [Theory]
    [InlineData("../outside", "C:\\outside.mp4")]
    [InlineData("_root", "../../outside.mp4")]
    [InlineData("CON", "NUL.mp4")]
    public void Remote_names_cannot_escape_the_library(string game, string name)
    {
        CloudService.ImportClips(new[] { Remote("safe-id", name, game) });
        using var con = Db.Open();
        var path = (string)con.QueryOne("SELECT filepath FROM clips")!["filepath"]!;
        Assert.StartsWith(_root + Path.DirectorySeparatorChar, Path.GetFullPath(path));
        Assert.DoesNotContain(Path.GetRelativePath(_root, path).Split(Path.DirectorySeparatorChar), part => part == "..");
    }

    private static GoogleDrive.DriveFile Remote(string id, string name, string game = "Test game", string duration = "0", string mtime = "1700000000")
        => new(id, name, "video/mp4", 123, DateTimeOffset.Parse("2026-10-22T10:00:00Z", CultureInfo.InvariantCulture), null, new Dictionary<string, string> { ["game"] = game, ["duration"] = duration, ["mtime"] = mtime });
}