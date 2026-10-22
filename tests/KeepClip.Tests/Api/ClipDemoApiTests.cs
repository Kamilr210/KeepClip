using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeepClip.Tests.Api;

[Collection("api")]
public class ClipDemoApiTests
{
    private readonly HttpClient _http;
    private readonly ApiFactory _factory;
    private readonly long _clip;
    private readonly string _directory = TestEnvironment.NewTempDir("cs2-game");

    public ClipDemoApiTests(ApiFactory factory)
    {
        TestDb.Reset();
        TestSettings.Reset();
        _http = factory.CreateClient();
        _factory = factory;
        _clip = TestDb.AddClip("Counter-Strike 2");
    }

    private string Demo(string filename = "faceit match.dem")
    {
        var path = Path.Combine(_directory, filename);
        File.WriteAllBytes(path, "PBDEMS2\0"u8.ToArray().Concat(new byte[24]).ToArray());
        return path;
    }

    private Task<HttpResponseMessage> Assign(string? path, long? clip = null)
        => _http.PutAsJsonAsync($"/api/clips/{clip ?? _clip}/demo", new { path });

    [Fact]
    public async Task Unassigned_clip_has_an_empty_demo_reference()
    {
        var status = await _http.GetJson($"/api/clips/{_clip}/demo");
        Assert.Equal(_clip, (long)status["clip_id"]!);
        Assert.Null(status["path"]);
        Assert.Null(status["filename"]);
        Assert.False((bool)status["available"]!);
    }

    [Fact]
    public async Task Assigning_a_demo_persists_only_its_path_and_does_not_modify_or_copy_the_file()
    {
        var path = Demo();
        var bytes = File.ReadAllBytes(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var filesBefore = Directory.GetFiles(TestEnvironment.DataDir, "*.dem", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
        var response = await Assign(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Json();
        Assert.Equal(path, (string)result["path"]!);
        Assert.Equal("faceit match.dem", (string)result["filename"]!);
        Assert.Equal(bytes.Length, (long)result["size_bytes"]!);
        Assert.True((bool)result["available"]!);
        Assert.Equal(path, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
        Assert.Equal(path, new ClipRepository().GetRow(_clip)!["demo_path"]);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(filesBefore, Directory.GetFiles(TestEnvironment.DataDir, "*.dem", SearchOption.AllDirectories).OrderBy(p => p));
    }

    [Fact]
    public async Task Replacing_and_detaching_a_demo_never_deletes_either_file()
    {
        var first = Demo("first.dem");
        var second = Demo("second.dem");
        Assert.Equal(HttpStatusCode.OK, (await Assign(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Assign(second)).StatusCode);
        Assert.Equal(second, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync($"/api/clips/{_clip}/demo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync($"/api/clips/{_clip}/demo")).StatusCode);
        Assert.Null((await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.NotNull(new ClipRepository().GetById(_clip));
    }

    [Fact]
    public async Task One_demo_can_be_linked_to_multiple_clips_and_survives_clip_deletion()
    {
        var secondClip = TestDb.AddClip("CS2");
        var path = Demo();
        Assert.Equal(HttpStatusCode.OK, (await Assign(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Assign(path, secondClip)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.DeleteAsync($"/api/clips/{_clip}?delete_file=false")).StatusCode);
        Assert.True(File.Exists(path));
        Assert.Equal(path, (string)(await _http.GetJson($"/api/clips/{secondClip}/demo"))["path"]!);
    }

    [Fact]
    public async Task A_moved_demo_remains_linked_and_is_reported_unavailable_until_reassigned()
    {
        var path = Demo();
        Assert.Equal(HttpStatusCode.OK, (await Assign(path)).StatusCode);
        var moved = Path.Combine(_directory, "moved.dem");
        File.Move(path, moved);
        var unavailable = await _http.GetJson($"/api/clips/{_clip}/demo");
        Assert.Equal(path, (string)unavailable["path"]!);
        Assert.False((bool)unavailable["available"]!);
        Assert.Null(unavailable["size_bytes"]);
        Assert.Equal(HttpStatusCode.OK, (await Assign(moved)).StatusCode);
        Assert.True((bool)(await _http.GetJson($"/api/clips/{_clip}/demo"))["available"]!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("match.dem")]
    [InlineData("C:match.dem")]
    [InlineData("\\\\server.invalid\\share\\match.dem")]
    [InlineData("//server.invalid/share/match.dem")]
    [InlineData("\\\\?\\C:\\match.dem")]
    [InlineData("\\\\.\\pipe\\match.dem")]
    [InlineData("file:///C:/match.dem")]
    [InlineData("C:\\match.dem:secret.dem")]
    [InlineData("C:\\match\r\n.dem")]
    public async Task Invalid_nonlocal_or_device_paths_are_rejected_without_changing_the_existing_link(string? invalid)
    {
        var original = Demo();
        Assert.Equal(HttpStatusCode.OK, (await Assign(original)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assign(invalid)).StatusCode);
        Assert.Equal(original, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
    }

    [Theory]
    [InlineData("recording.exe")]
    [InlineData("match.dem.exe")]
    [InlineData("match.dem.zip")]
    [InlineData("match.dem.zst")]
    public async Task Only_unpacked_dem_files_can_be_attached(string filename)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Assign(Demo(filename))).StatusCode);
        Assert.Null((await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PBDEMS2\0")]
    [InlineData("not a Source 2 demo at all")]
    [InlineData("HL2DEMO\0old Source 1 demo")]
    public async Task Empty_truncated_or_wrong_format_files_are_rejected(string content)
    {
        var path = Demo();
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(content));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Assign(path)).StatusCode);
    }

    [Fact]
    public async Task Missing_files_and_directories_cannot_be_attached()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Assign(Path.Combine(_directory, "missing.dem"))).StatusCode);
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "folder.dem"));
        Assert.Equal(HttpStatusCode.NotFound, (await Assign(directory.FullName)).StatusCode);
    }

    [Fact]
    public async Task Locked_file_is_rejected_without_losing_the_previous_reference()
    {
        var original = Demo("old.dem");
        var lockedPath = Demo("locked.dem");
        Assert.Equal(HttpStatusCode.OK, (await Assign(original)).StatusCode);
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Assign(lockedPath)).StatusCode);
        Assert.Equal(original, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
    }

    [Fact]
    public async Task Quoted_paths_uppercase_extension_and_apostrophes_are_preserved_safely()
    {
        var path = Demo("FACEIT player's match.DEM");
        Assert.Equal(HttpStatusCode.OK, (await Assign("  \"" + path + "\"  ")).StatusCode);
        Assert.Equal(path, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
        Assert.Equal(1, new ClipRepository().Count());
    }

    [Fact]
    public async Task Unknown_clip_cannot_get_assign_or_remove_a_demo()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/clips/999999/demo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Assign(Demo(), 999999)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync("/api/clips/999999/demo")).StatusCode);
    }

    [Theory]
    [InlineData("evil.invalid", null)]
    [InlineData("localhost", "http://evil.invalid")]
    [InlineData("localhost", "null")]
    [InlineData("localhost", "http://localhost:9999")]
    public async Task Foreign_hosts_or_origins_cannot_modify_local_file_references(string host, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/clips/{_clip}/demo") { Content = JsonContent.Create(new { path = Demo() }) };
        request.Headers.Host = host;
        if (origin is not null) request.Headers.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(request)).StatusCode);
        Assert.Null((await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]);
    }

    [Fact]
    public async Task Same_origin_requests_are_accepted_but_cross_site_requests_are_rejected()
    {
        using var allowed = new HttpRequestMessage(HttpMethod.Put, $"/api/clips/{_clip}/demo") { Content = JsonContent.Create(new { path = Demo() }) };
        allowed.Headers.Add("Origin", "http://localhost");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(allowed)).StatusCode);
        using var blocked = new HttpRequestMessage(HttpMethod.Delete, $"/api/clips/{_clip}/demo");
        blocked.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(blocked)).StatusCode);
        Assert.NotNull((await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]);
    }

    [Fact]
    public async Task Invalid_replacement_header_preserves_the_current_demo()
    {
        var original = Demo();
        var bad = Demo("bad.dem");
        File.WriteAllText(bad, "not a Source 2 demo");
        Assert.Equal(HttpStatusCode.OK, (await Assign(original)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Assign(bad)).StatusCode);
        Assert.Equal(original, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
        Assert.True(File.Exists(original));
    }

    [Fact]
    public async Task Cancellation_does_not_replace_an_existing_link()
    {
        var original = Demo();
        Assert.Equal(HttpStatusCode.OK, (await Assign(original)).StatusCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ClipDemoService(new ClipRepository());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssignAsync(_clip, Demo("new.dem"), cancellation.Token));
        Assert.Equal(original, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
    }

    [Fact]
    public async Task Cloud_clip_can_keep_a_local_demo_reference_without_connecting_to_google()
    {
        TestDb.Exec("UPDATE clips SET storage='cloud', remote_id='test-remote' WHERE id=$id", ("$id", _clip));
        var path = Demo();
        Assert.Equal(HttpStatusCode.OK, (await Assign(path)).StatusCode);
        Assert.Equal(path, (string)(await _http.GetJson($"/api/clips/{_clip}/demo"))["path"]!);
    }

    [Fact]
    public async Task Oversized_paths_and_missing_json_fields_are_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Assign("C:\\" + new string('a', 32768) + ".dem")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PutAsJsonAsync($"/api/clips/{_clip}/demo", new { })).StatusCode);
    }

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
        {
            builder.Use((http, nextMiddleware) => { http.Connection.RemoteIpAddress = address; return nextMiddleware(); });
            next(builder);
        };
    }

    [Theory]
    [InlineData("203.0.113.10", HttpStatusCode.Forbidden)]
    [InlineData("::1", HttpStatusCode.OK)]
    [InlineData("::ffff:127.0.0.1", HttpStatusCode.OK)]
    public async Task Only_loopback_connections_can_assign_a_demo_even_with_a_local_host_header(string address, HttpStatusCode expected)
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse(address)))));
        using var client = host.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/clips/{_clip}/demo", new { path = Demo() });
        Assert.Equal(expected, response.StatusCode);
    }
}