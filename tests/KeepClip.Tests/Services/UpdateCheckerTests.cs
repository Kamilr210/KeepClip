using System.Net;
using System.Text;

namespace KeepClip.Tests.Services;

public class UpdateCheckerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("https://api.github.com/repos/Kamilr210/KeepClip/releases/latest", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Release(string tag, string? url = "https://github.com/Kamilr210/KeepClip/releases/download/v2.4.0/KeepClip-Setup.exe")
        => new(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, html_url = "https://github.com/Kamilr210/KeepClip/releases/tag/" + tag, assets = new[] { new { name = "other.zip", browser_download_url = (string?)"ignored" }, new { name = "KeepClip-Setup.exe", browser_download_url = url } } }), Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("v2.4.0", true, "2.4.0")]
    [InlineData("V2.4", true, "2.4.0")]
    [InlineData("2.3.0", false, "2.3.0")]
    [InlineData("v2.2.9", false, "2.2.9")]
    public async Task Release_comparison_reports_only_newer_installable_versions(string tag, bool available, string latest)
    {
        using var handler = new Handler(() => Release(tag));
        using var http = new HttpClient(handler);
        var result = await new UpdateChecker(http, new Version(2, 3, 0), new Clock()).CheckAsync();
        Assert.Equal(true, result["ok"]);
        Assert.Equal(available, result["available"]);
        Assert.Equal(latest, result["latest"]);
        Assert.Equal("2.3.0", result["current"]);
        Assert.EndsWith("KeepClip-Setup.exe", (string)result["url"]!);
    }

    [Theory]
    [InlineData("bad-version")]
    [InlineData("")]
    public async Task Invalid_version_is_a_failed_check_without_an_available_update(string tag)
    {
        using var handler = new Handler(() => Release(tag));
        using var http = new HttpClient(handler);
        var result = await new UpdateChecker(http, new Version(2, 3, 0), new Clock()).CheckAsync();
        Assert.Equal(false, result["ok"]);
        Assert.Equal(false, result["available"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"v2.4.0\"}")]
    public async Task Incomplete_release_metadata_never_produces_an_installable_update(string json)
    {
        using var handler = new Handler(() => new(HttpStatusCode.OK) { Content = new StringContent(json) });
        using var http = new HttpClient(handler);
        var result = await new UpdateChecker(http, new Version(2, 3, 0), new Clock()).CheckAsync();
        Assert.Equal(false, result["available"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a URL")]
    [InlineData("http://example.invalid/installer.exe")]
    public async Task Missing_installer_url_is_not_an_available_update(string? url)
    {
        using var handler = new Handler(() => Release("v2.4.0", url));
        using var http = new HttpClient(handler);
        var result = await new UpdateChecker(http, new Version(2, 3, 0), new Clock()).CheckAsync();
        Assert.Equal(false, result["available"]);
    }

    [Fact]
    public async Task Successful_checks_are_cached_for_six_hours()
    {
        var clock = new Clock();
        using var handler = new Handler(() => Release("v2.4.0"));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, new Version(2, 3, 0), clock);
        await checker.CheckAsync();
        clock.Now += TimeSpan.FromHours(6) - TimeSpan.FromSeconds(1);
        await checker.CheckAsync();
        Assert.Equal(1, handler.Calls);
        clock.Now += TimeSpan.FromSeconds(1);
        await checker.CheckAsync();
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_checks_are_retried_after_thirty_minutes(bool throws)
    {
        var clock = new Clock();
        bool fail = true;
        using var handler = new Handler(() => fail ? throws ? throw new HttpRequestException("offline") : new(HttpStatusCode.BadGateway) : Release("v2.4.0"));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, new Version(2, 3, 0), clock);
        Assert.Equal(false, (await checker.CheckAsync())["ok"]);
        fail = false;
        clock.Now += TimeSpan.FromMinutes(29);
        Assert.Equal(false, (await checker.CheckAsync())["ok"]);
        Assert.Equal(1, handler.Calls);
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.Equal(true, (await checker.CheckAsync())["available"]);
        Assert.Equal(2, handler.Calls);
    }
}