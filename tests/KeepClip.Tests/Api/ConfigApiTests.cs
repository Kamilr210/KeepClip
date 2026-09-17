using System.Net;
using System.Text.RegularExpressions;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class ConfigApiTests
{
    private readonly HttpClient _http;

    public ConfigApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task Ping_answers()
    {
        var json = await _http.GetJson("/api/ping");
        Assert.True((bool)json["ok"]!);
    }

    [Fact]
    public async Task Index_serves_the_interface_with_cache_busting_and_theme()
    {
        var html = await _http.GetStringAsync("/");

        Assert.Contains("<title>KeepClip", html);
        Assert.Matches(@"/static/app\.js\?v=\d+", html);
        Assert.Matches(@"/static/onboarding\.js\?v=\d+", html);
        Assert.Contains("data-accent-theme=\"purple\"", html);
    }

    [Fact]
    public async Task Static_files_are_served()
    {
        var response = await _http.GetAsync("/static/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Fresh_config_is_unconfigured_and_english()
    {
        var json = await _http.GetJson("/api/config");

        Assert.False((bool)json["configured"]!);
        Assert.Equal("en", (string)json["language"]!);
        Assert.Equal("purple", (string)json["accent_theme"]!);
        Assert.Matches(@"^\d+\.\d+\.\d+$", (string)json["version"]!);
        Assert.NotNull(json["clips_root"]);
    }

    [Fact]
    public async Task Setting_clips_root_validates_the_path()
    {
        var empty = await _http.PostJson("/api/config", new { clips_root = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("configPathRequired", (string)(await empty.Json())["error"]!);

        var missing = await _http.PostJson("/api/config", new { clips_root = @"C:\na-pewno\nie-ma\takiego" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var file = Path.Combine(TestEnvironment.NewTempDir("plik"), "plik.txt");
        File.WriteAllText(file, "x");
        var notFolder = await _http.PostJson("/api/config", new { clips_root = file });
        Assert.Equal(HttpStatusCode.BadRequest, notFolder.StatusCode);
    }

    [Fact]
    public async Task Setting_clips_root_scans_it_and_marks_configuration_done()
    {
        var root = TestEnvironment.NewTempDir("root");
        var clip = Path.Combine(root, "Gra", "a.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
        File.WriteAllBytes(clip, new byte[4]);
        File.SetLastWriteTimeUtc(clip, DateTime.UtcNow.AddSeconds(-10));

        var response = await _http.PostJson("/api/config", new { clips_root = $"\"{root}\"" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Json();
        Assert.Equal(root, (string)json["clips_root"]!);
        Assert.Equal(1, (int)json["scan"]!["added"]!);

        var config = await _http.GetJson("/api/config");
        Assert.True((bool)config["configured"]!);
        Assert.True((bool)config["clips_root_exists"]!);
        Assert.Single((await _http.GetJson("/api/clips")).AsArray());
    }

    [Fact]
    public async Task Language_accepts_only_supported_codes()
    {
        var bad = await _http.PostJson("/api/language", new { language = "xx" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("configBadLanguage", (string)(await bad.Json())["error"]!);

        var ok = await _http.PostJson("/api/language", new { language = "PL" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("pl", (string)(await _http.GetJson("/api/config"))["language"]!);
    }

    [Fact]
    public async Task Accent_theme_changes_the_served_page()
    {
        var bad = await _http.PostJson("/api/accent-theme", new { theme = "red" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await _http.PostJson("/api/accent-theme", new { theme = "green" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var html = await _http.GetStringAsync("/");
        Assert.Contains("data-accent-theme=\"green\"", html);
        Assert.Contains("favicon-green.svg", html);
        Assert.Equal("green", (string)(await _http.GetJson("/api/config"))["accent_theme"]!);
    }

    [Fact]
    public async Task Heartbeat_and_scan_respond()
    {
        Settings.SetClipsRoot(TestEnvironment.NewTempDir("pusty"));

        var heartbeat = await _http.PostEmpty("/api/heartbeat");
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);

        var scan = await _http.PostEmpty("/api/scan");
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal(0, (int)(await scan.Json())["found"]!);
    }

    [Fact]
    public async Task Unknown_api_route_is_404()
    {
        var response = await _http.GetAsync("/api/nie-ma-takiego");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
