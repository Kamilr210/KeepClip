namespace KeepClip.Tests.Data;

public class FolderRepositoryTests
{
    private readonly FolderRepository _folders = new();

    public FolderRepositoryTests() => TestDb.Reset();

    private long Create(string name) => Convert.ToInt64(_folders.Create(name)!["id"]);

    [Fact]
    public void Create_returns_row_and_rejects_duplicate_name()
    {
        var row = _folders.Create("Najlepsze");

        Assert.NotNull(row);
        Assert.Equal("Najlepsze", row["name"]);
        Assert.NotNull(row["created_at"]);
        Assert.Null(_folders.Create("Najlepsze"));
    }

    [Fact]
    public void List_sorts_by_name_ignoring_case_and_counts_clips()
    {
        var b = Create("beta");
        Create("Alfa");
        var clip1 = TestDb.AddClip();
        var clip2 = TestDb.AddClip();
        _folders.AddClip(b, clip1);
        _folders.AddClip(b, clip2);

        var list = _folders.List();

        Assert.Equal(new object?[] { "Alfa", "beta" }, list.Select(f => f["name"]).ToList());
        var beta = list[1];
        Assert.Equal(2L, Convert.ToInt64(beta["clip_count"]));
        var samples = Assert.IsType<List<object?>>(beta["sample_clip_ids"]);
        Assert.Equal(2, samples.Count);
        Assert.Equal(0L, Convert.ToInt64(list[0]["clip_count"]));
    }

    [Fact]
    public void Rename_reports_missing_folder_and_duplicates()
    {
        var a = Create("A");
        Create("B");

        Assert.Equal(FolderWrite.Ok, _folders.Rename(a, "C"));
        Assert.Equal(FolderWrite.Duplicate, _folders.Rename(a, "B"));
        Assert.Equal(FolderWrite.NotFound, _folders.Rename(999, "X"));
        Assert.Equal("C", _folders.List().Single(f => Convert.ToInt64(f["id"]) == a)["name"]);
    }

    [Fact]
    public void Delete_returns_name_once_and_removes_links()
    {
        var id = Create("Do usunięcia");
        var clip = TestDb.AddClip();
        _folders.AddClip(id, clip);

        Assert.Equal("Do usunięcia", _folders.Delete(id));

        Assert.Null(_folders.Delete(id));
        Assert.Empty(_folders.GetFolderIdsForClip(clip));
        Assert.NotNull(new ClipRepository().GetById(clip));
    }

    [Fact]
    public void AddClip_distinguishes_missing_folder_missing_clip_and_success()
    {
        var folder = Create("F");
        var clip = TestDb.AddClip();

        Assert.Equal(AddClipResult.NoFolder, _folders.AddClip(999, clip));
        Assert.Equal(AddClipResult.NoClip, _folders.AddClip(folder, 999));
        Assert.Equal(AddClipResult.Ok, _folders.AddClip(folder, clip));
        Assert.Equal(AddClipResult.Ok, _folders.AddClip(folder, clip));
        Assert.Equal(1L, TestDb.ScalarLong("SELECT COUNT(*) FROM folder_clips"));
    }

    [Fact]
    public void RemoveClip_and_GetFolderIdsForClip_work_together()
    {
        var f1 = Create("F1");
        var f2 = Create("F2");
        var clip = TestDb.AddClip();
        _folders.AddClip(f1, clip);
        _folders.AddClip(f2, clip);

        Assert.Equal(new[] { f1, f2 }, _folders.GetFolderIdsForClip(clip).OrderBy(x => x));

        _folders.RemoveClip(f1, clip);

        Assert.Equal(new[] { f2 }, _folders.GetFolderIdsForClip(clip));
    }

    [Fact]
    public void ListClips_returns_null_for_unknown_folder_and_sorts_clips()
    {
        var folder = Create("F");
        var older = TestDb.AddClip(size: 5, mtime: 100);
        var newer = TestDb.AddClip(size: 1, mtime: 200);
        _folders.AddClip(folder, older);
        _folders.AddClip(folder, newer);

        Assert.Null(_folders.ListClips(999, null, 10));

        var (row, clips) = _folders.ListClips(folder, null, 10)!.Value;
        Assert.Equal("F", row["name"]);
        Assert.Equal(newer, Convert.ToInt64(clips[0]["id"]));

        var largest = _folders.ListClips(folder, "largest", 10)!.Value.clips;
        Assert.Equal(older, Convert.ToInt64(largest[0]["id"]));
        Assert.Single(_folders.ListClips(folder, null, 1)!.Value.clips);
    }
}
