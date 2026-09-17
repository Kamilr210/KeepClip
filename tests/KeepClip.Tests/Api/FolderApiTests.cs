using System.Net;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class FolderApiTests
{
    private readonly HttpClient _http;

    public FolderApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    private async Task<long> CreateFolder(string name)
    {
        var response = await _http.PostJson("/api/folders", new { name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (long)(await response.Json())["id"]!;
    }

    [Fact]
    public async Task Create_validates_name_and_rejects_duplicates()
    {
        var empty = await _http.PostJson("/api/folders", new { name = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooLong = await _http.PostJson("/api/folders", new { name = new string('x', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("folderNameTooLong", (string)(await tooLong.Json())["error"]!);

        await CreateFolder("Najlepsze");
        var duplicate = await _http.PostJson("/api/folders", new { name = " Najlepsze " });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("folderDuplicate", (string)(await duplicate.Json())["error"]!);
    }

    [Fact]
    public async Task Full_folder_lifecycle_over_http()
    {
        var id = await CreateFolder("Do montażu");
        var clip = TestDb.AddClip();

        var list = (await _http.GetJson("/api/folders")).AsArray();
        Assert.Single(list);
        Assert.Equal("Do montażu", (string)list[0]!["name"]!);
        Assert.Equal(0, (int)list[0]!["clip_count"]!);

        var rename = await _http.PatchJson($"/api/folders/{id}", new { name = "Gotowe" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("Gotowe", (string)(await rename.Json())["name"]!);

        var add = await _http.PostEmpty($"/api/folders/{id}/clips/{clip}");
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var clips = await _http.GetJson($"/api/folders/{id}/clips");
        Assert.Equal("Gotowe", (string)clips["folder"]!["name"]!);
        Assert.Single(clips["clips"]!.AsArray());

        var ids = (await _http.GetJson($"/api/clips/{clip}/folders"))["folder_ids"]!.AsArray();
        Assert.Equal(id, (long)ids[0]!);

        var remove = await _http.DeleteAsync($"/api/folders/{id}/clips/{clip}");
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Empty((await _http.GetJson($"/api/folders/{id}/clips"))["clips"]!.AsArray());

        var delete = await _http.DeleteAsync($"/api/folders/{id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal("Gotowe", (string)(await delete.Json())["deleted_name"]!);
        Assert.Empty((await _http.GetJson("/api/folders")).AsArray());
    }

    [Fact]
    public async Task Missing_folder_or_clip_returns_404()
    {
        var id = await CreateFolder("F");

        Assert.Equal(HttpStatusCode.NotFound, (await _http.PatchJson("/api/folders/999", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync("/api/folders/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/folders/999/clips")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostEmpty("/api/folders/999/clips/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostEmpty($"/api/folders/{id}/clips/999")).StatusCode);
    }

    [Fact]
    public async Task Renaming_to_an_existing_name_conflicts()
    {
        var a = await CreateFolder("A");
        await CreateFolder("B");

        var response = await _http.PatchJson($"/api/folders/{a}", new { name = "B" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
