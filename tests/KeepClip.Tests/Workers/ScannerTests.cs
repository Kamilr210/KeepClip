namespace KeepClip.Tests.Workers;

public class ScannerTests
{
    private readonly string _root;
    private readonly ClipRepository _clips = new();

    public ScannerTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        _root = TestEnvironment.NewTempDir("clips");
        Settings.SetClipsRoot(_root);
    }

    private string AddFile(string relativePath, int bytes = 8, bool settled = true)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (settled) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
        return path;
    }

    [Fact]
    public void First_scan_adds_videos_and_names_games_after_their_folder()
    {
        AddFile(@"Counter-Strike 2\a.mp4");
        AddFile(@"Counter-Strike 2\b.mkv");
        AddFile("luzem.mp4");
        AddFile(@"Counter-Strike 2\notatki.txt");

        var result = Scanner.Scan();

        Assert.Equal(3, result["found"]);
        Assert.Equal(3, result["added"]);
        Assert.Equal(0, result["removed"]);
        var games = _clips.List(null, null, 10, false).Select(r => (string)r["game"]!).OrderBy(g => g, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Counter-Strike 2", "Counter-Strike 2", "_root" }, games);
    }

    [Fact]
    public void Second_scan_changes_nothing()
    {
        AddFile(@"Gra\a.mp4");
        Scanner.Scan();

        var result = Scanner.Scan();

        Assert.Equal(1, result["found"]);
        Assert.Equal(0, result["added"]);
        Assert.Equal(0, result["updated"]);
        Assert.Equal(1, _clips.Count());
    }

    [Fact]
    public void Freshly_written_files_wait_for_the_next_scan()
    {
        var path = AddFile(@"Gra\nagrywany.mp4", settled: false);

        var first = Scanner.Scan();
        Assert.Equal(1, first["found"]);
        Assert.Equal(0, first["added"]);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
        Assert.Equal(1, Scanner.Scan()["added"]);
    }

    [Fact]
    public void Deleted_files_leave_the_library_with_their_segments()
    {
        var path = AddFile(@"Gra\a.mp4");
        Scanner.Scan();
        var id = Convert.ToInt64(_clips.List(null, null, 10, false)[0]["id"]);
        new SegmentRepository().Insert(id, 0, 1, "x");
        File.Delete(path);

        var result = Scanner.Scan();

        Assert.Equal(1, result["removed"]);
        Assert.Equal(0, _clips.Count());
        Assert.Empty(new SegmentRepository().ListByClipId(id));
    }

    [Fact]
    public void Clips_moved_to_the_cloud_survive_a_missing_file()
    {
        var path = AddFile(@"Gra\chmura.mp4");
        Scanner.Scan();
        TestDb.Exec("UPDATE clips SET storage='cloud'");
        File.Delete(path);

        var result = Scanner.Scan();

        Assert.Equal(0, result["removed"]);
        Assert.Equal(1, _clips.Count());
    }

    [Fact]
    public void Changed_file_size_refreshes_the_entry()
    {
        var path = AddFile(@"Gra\a.mp4", bytes: 8);
        Scanner.Scan();
        File.WriteAllBytes(path, new byte[64]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));

        var result = Scanner.Scan();

        Assert.Equal(1, result["updated"]);
        Assert.Equal(64L, Convert.ToInt64(_clips.List(null, null, 10, false)[0]["size_bytes"]));
    }

    [Fact]
    public void Missing_root_folder_reports_an_error_instead_of_throwing()
    {
        Settings.SetClipsRoot(Path.Combine(_root, "nie-ma"));

        var result = Scanner.Scan();

        Assert.Equal(0, result["found"]);
        Assert.NotNull(result["error"]);
    }
}
