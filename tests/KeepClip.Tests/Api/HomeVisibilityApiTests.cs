using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class HomeVisibilityApiTests
{
    private readonly HttpClient _http;
    private readonly FolderRepository _folders = new();

    public HomeVisibilityApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    private long CreateFolder(string name) => Convert.ToInt64(_folders.Create(name)!["id"]);

    private async Task SetGlobalVisibility(bool hidden)
    {
        var response = await _http.PostJson("/api/home-visibility", new { hide_folder_clips = hidden });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SetFolderVisibility(long folder, bool hidden)
    {
        var response = await _http.PatchJson($"/api/folders/{folder}/visibility", new { hidden_from_home = hidden });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static long[] Ids(JsonNode rows, string key = "id") => rows.AsArray().Select(row => (long)row![key]!).ToArray();

    [Fact]
    public async Task Global_visibility_defaults_to_false_and_persists_both_values()
    {
        Assert.False((bool)(await _http.GetJson("/api/config"))["hide_folder_clips"]!);
        Settings.SetLanguage("pl");

        await SetGlobalVisibility(true);

        Assert.True((bool)(await _http.GetJson("/api/config"))["hide_folder_clips"]!);
        Assert.True((bool)JsonNode.Parse(File.ReadAllText(TestSettings.Path))!["hide_folder_clips"]!);

        await SetGlobalVisibility(false);

        var config = await _http.GetJson("/api/config");
        Assert.False((bool)config["hide_folder_clips"]!);
        Assert.False((bool)JsonNode.Parse(File.ReadAllText(TestSettings.Path))!["hide_folder_clips"]!);
        Assert.Equal("pl", (string)config["language"]!);
    }

    [Fact]
    public async Task Folder_visibility_defaults_to_false_and_is_returned_in_list_and_details()
    {
        var created = await _http.PostJson("/api/folders", new { name = "Gotowe" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var folder = await created.Json();
        var id = (long)folder["id"]!;
        Assert.False((bool)folder["hidden_from_home"]!);

        await SetFolderVisibility(id, true);

        Assert.True((bool)(await _http.GetJson("/api/folders"))[0]!["hidden_from_home"]!);
        Assert.True((bool)(await _http.GetJson($"/api/folders/{id}/clips"))["folder"]!["hidden_from_home"]!);
        Assert.Equal(1L, TestDb.ScalarLong("SELECT hidden_from_home FROM folders WHERE id=$id", ("$id", id)));

        await SetFolderVisibility(id, false);

        Assert.False((bool)(await _http.GetJson("/api/folders"))[0]!["hidden_from_home"]!);
        Assert.False((bool)(await _http.GetJson($"/api/folders/{id}/clips"))["folder"]!["hidden_from_home"]!);
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("{\"hide_folder_clips\":null}", "{\"hidden_from_home\":null}")]
    [InlineData("{\"hide_folder_clips\":\"false\"}", "{\"hidden_from_home\":\"false\"}")]
    [InlineData("{\"hide_folder_clips\":0}", "{\"hidden_from_home\":0}")]
    [InlineData("{", "{")]
    public async Task Invalid_visibility_payloads_return_400_without_changing_saved_values(string globalBody, string folderBody)
    {
        var folder = CreateFolder("Ukryty");
        await SetGlobalVisibility(true);
        await SetFolderVisibility(folder, true);

        var global = await _http.PostAsync("/api/home-visibility", new StringContent(globalBody, Encoding.UTF8, "application/json"));
        var local = await _http.PatchAsync($"/api/folders/{folder}/visibility", new StringContent(folderBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, global.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, local.StatusCode);
        Assert.True((bool)(await _http.GetJson("/api/config"))["hide_folder_clips"]!);
        Assert.True((bool)(await _http.GetJson("/api/folders"))[0]!["hidden_from_home"]!);
    }

    [Fact]
    public async Task Changing_visibility_of_missing_folder_returns_404()
    {
        var response = await _http.PatchJson("/api/folders/999/visibility", new { hidden_from_home = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await _http.GetJson("/api/folders")).AsArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Home_hides_matching_memberships_but_keeps_favorites_and_folder_contents_accessible(bool global)
    {
        var visibleFolder = CreateFolder("Widoczny");
        var hiddenFolder = CreateFolder("Ukryty");
        var free = TestDb.AddClip(mtime: 1);
        var visible = TestDb.AddClip(mtime: 2);
        var hidden = TestDb.AddClip(mtime: 3);
        var shared = TestDb.AddClip(mtime: 4);
        _folders.AddClip(visibleFolder, visible);
        _folders.AddClip(hiddenFolder, hidden);
        _folders.AddClip(visibleFolder, shared);
        _folders.AddClip(hiddenFolder, shared);
        foreach (var clip in new[] { free, visible, hidden, shared })
        {
            new ClipRepository().ToggleFavorite(clip);
            new SegmentRepository().Insert(clip, 0, 1, "headshot");
        }
        await SetFolderVisibility(hiddenFolder, true);
        await SetGlobalVisibility(global);

        Assert.Equal(global ? new[] { free } : new[] { visible, free }, Ids(await _http.GetJson("/api/clips?home=true")));
        Assert.Equal(global ? new[] { free } : new[] { visible, free }, Ids((await _http.GetJson("/api/search?q=head&home=true&sort=newest"))["results"]!, "clip_id"));
        Assert.Equal(new[] { shared, hidden, visible, free }, Ids(await _http.GetJson("/api/clips")));
        Assert.Equal(new[] { shared, hidden, visible, free }, Ids(await _http.GetJson("/api/clips?favorite=1")));
        Assert.Equal(new[] { shared, hidden, visible, free }, Ids(await _http.GetJson("/api/clips?home=false")));
        Assert.Equal(new[] { shared, hidden }, Ids((await _http.GetJson($"/api/folders/{hiddenFolder}/clips"))["clips"]!));
    }

    [Fact]
    public async Task Global_toggle_preserves_per_folder_choices()
    {
        var folder = CreateFolder("Ukryty");
        var clip = TestDb.AddClip();
        _folders.AddClip(folder, clip);
        await SetFolderVisibility(folder, true);
        await SetGlobalVisibility(true);

        await SetGlobalVisibility(false);

        Assert.True((bool)(await _http.GetJson("/api/folders"))[0]!["hidden_from_home"]!);
        Assert.Empty((await _http.GetJson("/api/clips?home=true")).AsArray());

        await SetFolderVisibility(folder, false);

        Assert.Equal(new[] { clip }, Ids(await _http.GetJson("/api/clips?home=true")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_membership_or_deleting_folder_restores_home_visibility(bool global)
    {
        var first = CreateFolder("Pierwszy");
        var second = CreateFolder("Drugi");
        var clip = TestDb.AddClip();
        _folders.AddClip(first, clip);
        _folders.AddClip(second, clip);
        await SetFolderVisibility(first, true);
        await SetFolderVisibility(second, true);
        await SetGlobalVisibility(global);
        Assert.Empty((await _http.GetJson("/api/clips?home=true")).AsArray());

        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync($"/api/folders/{first}/clips/{clip}")).StatusCode);
        Assert.Empty((await _http.GetJson("/api/clips?home=true")).AsArray());

        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync($"/api/folders/{second}")).StatusCode);
        Assert.Equal(new[] { clip }, Ids(await _http.GetJson("/api/clips?home=true")));
        Assert.NotNull(new ClipRepository().GetById(clip));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Home_filter_runs_before_clip_and_search_limits_without_hiding_unscoped_search(bool global)
    {
        var folder = CreateFolder("Ukryty");
        var older = TestDb.AddClip("CS2", mtime: 1);
        var hidden = TestDb.AddClip("CS2", mtime: 2);
        var otherGame = TestDb.AddClip("Factorio", mtime: 3);
        _folders.AddClip(folder, hidden);
        await SetFolderVisibility(folder, true);
        await SetGlobalVisibility(global);
        foreach (var clip in new[] { older, hidden, otherGame }) new SegmentRepository().Insert(clip, 0, 1, "niesamowity headshot");

        Assert.Equal(new[] { older }, Ids(await _http.GetJson("/api/clips?home=true&game=CS2&limit=1")));
        var search = await _http.GetJson("/api/search?q=head&home=true&sort=oldest&limit=2");
        Assert.Equal(new[] { older, otherGame }, Ids(search["results"]!, "clip_id"));
        Assert.Contains("<mark>", (string)search["results"]![0]!["snippet"]!);
        Assert.Equal(new[] { older, hidden, otherGame }, Ids((await _http.GetJson("/api/search?q=head&sort=oldest"))["results"]!, "clip_id"));
    }
}