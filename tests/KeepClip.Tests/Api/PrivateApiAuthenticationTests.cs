using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class PrivateApiAuthenticationTests
{
    private readonly ApiFactory _factory;

    public PrivateApiAuthenticationTests(ApiFactory factory)
    {
        _factory = factory;
        TestDb.Reset();
        TestSettings.Reset();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/static/app.js")]
    [InlineData("/api/config")]
    [InlineData("/api/clips")]
    [InlineData("/api/replay/status")]
    [InlineData("/api/cloud/status")]
    [InlineData("/api/transcribe/status")]
    [InlineData("/api/ping")]
    [InlineData("/video/999")]
    [InlineData("/playback/999")]
    [InlineData("/thumb/999")]
    public async Task Requests_without_owner_credentials_cannot_read_any_private_surface(string path)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_different_local_caller_cannot_delete_a_library_record()
    {
        var id = TestDb.AddClip();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('a', 64));
        var response = await client.DeleteAsync($"/api/clips/{id}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(new ClipRepository().GetById(id));
    }

    [Theory]
    [InlineData("Origin", "http://evil.invalid")]
    [InlineData("Origin", "http://localhost:9999")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData("Referer", "http://localhost:9999/attack")]
    public async Task Browser_origin_protection_is_required_even_with_valid_authentication(string header, string value)
    {
        using var client = _factory.CreateClient();
        var previous = Settings.GetString("accent_theme");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accent-theme") { Content = JsonContent.Create(new { theme = "green" }) };
        request.Headers.TryAddWithoutValidation(header, value);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(previous, Settings.GetString("accent_theme"));
    }

    [Fact]
    public async Task Authenticated_media_supports_range_requests_but_query_strings_and_cookies_do_not_authenticate()
    {
        var directory = TestEnvironment.NewTempDir("authenticated-video");
        File.WriteAllBytes(Path.Combine(directory, "clip.mp4"), new byte[] { 10, 20, 30, 40 });
        var id = TestDb.AddClip(name: "clip.mp4", dir: directory);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Range = new RangeHeaderValue(1, 2);
        var response = await client.GetAsync($"/video/{id}");
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(new byte[] { 20, 30 }, await response.Content.ReadAsByteArrayAsync());
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("Cookie", "KeepClip=" + TestEnvironment.ApiToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/video/{id}?token={TestEnvironment.ApiToken}")).StatusCode);
    }

    [Fact]
    public async Task Server_browser_login_accepts_only_the_owner_key_and_never_embeds_it_in_html()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("keepclip:" + TestEnvironment.ApiToken)));
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(TestEnvironment.ApiToken, await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "invalid-base64");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Unauthorized_capture_request_is_rejected_before_validation_or_settings_changes()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        // Niepoprawne fps chroni maszynę testową przed nagrywaniem także po regresji w autoryzacji.
        var response = await client.PostJson("/api/replay/config", new { enabled = true, mic = true, fps = -1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(Settings.GetString("replay_enabled"));
        Assert.Null(Settings.GetString("replay_mic"));
        Assert.False(ReplayService.Enabled);
    }
}