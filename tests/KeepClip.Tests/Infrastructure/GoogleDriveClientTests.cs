using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace KeepClip.Tests.Infrastructure;

public class GoogleDriveClientTests
{
    private sealed record Request(HttpMethod Method, Uri Uri, string? Authorization, byte[] Body, string? ContentType, string? Range);

    private sealed class Handler(params Func<Request, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<Request, HttpResponseMessage>> _responses = new(responses);
        public List<Request> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var captured = new Request(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct), request.Content?.Headers.ContentType?.MediaType, request.Headers.Range?.ToString());
            Requests.Add(captured);
            Assert.NotEmpty(_responses);
            return _responses.Dequeue()(captured);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static Dictionary<string, string> Form(Request request) => Encoding.UTF8.GetString(request.Body).Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    [Fact]
    public async Task Authorization_code_exchange_sends_pkce_and_preserves_encoded_values()
    {
        using var handler = new Handler(_ => Json("{\"access_token\":\"test-access\",\"refresh_token\":\"test-refresh\",\"expires_in\":120}"));
        using var http = new HttpClient(handler);
        var client = new GoogleDriveClient(http, http);

        var token = await client.ExchangeCodeAsync("id+1", "a=b", "verifier /", "http://127.0.0.1:1234/");

        Assert.Equal(new GoogleDrive.TokenResult("test-access", "test-refresh", 120), token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(GoogleDrive.TokenEndpoint, request.Uri.AbsoluteUri);
        Assert.Null(request.Authorization);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        var form = Form(request);
        Assert.Equal("id+1", form["client_id"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.Equal("a=b", form["code"]);
        Assert.Equal("verifier /", form["code_verifier"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("http://127.0.0.1:1234/", form["redirect_uri"]);
    }

    [Fact]
    public async Task Refresh_accepts_missing_optional_fields_and_sends_the_refresh_token()
    {
        using var handler = new Handler(_ => Json("{\"access_token\":\"new-access\"}"));
        using var http = new HttpClient(handler);

        var token = await new GoogleDriveClient(http, http).RefreshAsync("id", "refresh+&");

        Assert.Equal(new GoogleDrive.TokenResult("new-access", null, 3600), token);
        var form = Form(Assert.Single(handler.Requests));
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("refresh+&", form["refresh_token"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.False(form.ContainsKey("code_verifier"));
    }

    [Theory]
    [InlineData("{\"error_description\":\"invalid_grant\"}", "invalid_grant", true)]
    [InlineData("{\"error\":\"invalid_grant\"}", "invalid_grant", true)]
    [InlineData("{\"error\":{\"message\":\"quota exceeded\"}}", "quota exceeded", false)]
    [InlineData("not json", "HTTP 400 Bad Request", false)]
    [InlineData("{}", "HTTP 400 Bad Request", false)]
    public async Task Api_errors_are_reported_without_returning_a_successful_result(string json, string message, bool invalidGrant)
    {
        using var handler = new Handler(_ => Json(json, HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<GoogleDrive.GoogleApiException>(() => new GoogleDriveClient(http, http).RefreshAsync("id", "refresh"));

        Assert.Equal(message, error.Message);
        Assert.Equal(invalidGrant, error.IsInvalidGrant);
    }

    [Fact]
    public async Task Revocation_is_best_effort_but_uses_the_correct_token()
    {
        using var handler = new Handler(_ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler);

        await new GoogleDriveClient(http, http).RevokeAsync("test-refresh");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(GoogleDrive.RevokeEndpoint, request.Uri.AbsoluteUri);
        Assert.Equal("test-refresh", Form(request)["token"]);
    }

    [Theory]
    [InlineData("{}", null, null, null)]
    [InlineData("{\"user\":{\"displayName\":\"Tester\",\"emailAddress\":\"test@example.invalid\",\"photoLink\":\"https://lh3.googleusercontent.com/test\"},\"storageQuota\":{\"usage\":\"123\",\"limit\":\"456\"}}", "Tester", 123L, 456L)]
    [InlineData("{\"user\":{},\"storageQuota\":{\"usage\":\"invalid\",\"limit\":\"invalid\"}}", null, null, null)]
    public async Task About_handles_optional_profile_and_quota_fields(string body, string? name, long? usage, long? limit)
    {
        using var handler = new Handler(_ => Json(body));
        using var http = new HttpClient(handler);

        var result = await new GoogleDriveClient(http, http).GetAboutAsync("test-access");

        Assert.Equal(name, result.Name);
        Assert.Equal(usage, result.Usage);
        Assert.Equal(limit, result.Limit);
        Assert.Equal("Bearer test-access", Assert.Single(handler.Requests).Authorization);
        if (name is not null) Assert.Equal("test@example.invalid", result.Email);
    }

    [Theory]
    [InlineData("http://lh3.googleusercontent.com/a")]
    [InlineData("https://googleusercontent.com.evil.invalid/a")]
    [InlineData("https://evilgoogleusercontent.com/a")]
    [InlineData("file:///C:/test")]
    [InlineData("/relative")]
    public async Task Profile_photo_rejects_untrusted_addresses_before_sending_a_request(string url)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GoogleDriveClient(http, http).DownloadProfilePhotoAsync(url));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://googleusercontent.com/a")]
    [InlineData("https://lh3.googleusercontent.com/a")]
    [InlineData("https://ggpht.com/a")]
    [InlineData("https://lh3.ggpht.com/a")]
    public async Task Profile_photo_accepts_trusted_hosts_without_sending_the_access_token(string url)
    {
        using var handler = new Handler(_ => Image(new byte[] { 1, 2, 3 }, "image/png"));
        using var http = new HttpClient(handler);

        var photo = await new GoogleDriveClient(http, http).DownloadProfilePhotoAsync(url);

        Assert.Equal(new byte[] { 1, 2, 3 }, photo.Data);
        Assert.Equal("image/png", photo.ContentType);
        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }

    [Theory]
    [InlineData(0, "image/png")]
    [InlineData(1048577, "image/png")]
    [InlineData(10, "text/html")]
    [InlineData(10, "image/svg+xml")]
    public async Task Profile_photo_rejects_empty_oversized_or_non_image_responses(int size, string contentType)
    {
        using var handler = new Handler(_ => Image(new byte[size], contentType));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GoogleDriveClient(http, http).DownloadProfilePhotoAsync("https://lh3.googleusercontent.com/test"));
    }

    [Fact]
    public async Task Known_folder_is_reused_without_searching_or_creating_a_duplicate()
    {
        using var handler = new Handler(_ => Json("{\"id\":\"folder-id\",\"trashed\":false}"));
        using var http = new HttpClient(handler);
        Assert.Equal("folder-id", await new GoogleDriveClient(http, http).EnsureFolderAsync("token", "KeepClip", "folder-id"));
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"id\":\"old\",\"trashed\":true}")]
    public async Task Missing_or_trashed_folder_is_looked_up_before_creation(HttpStatusCode status, string body)
    {
        using var handler = new Handler(_ => Json(body, status), _ => Json("{\"files\":[{\"id\":\"existing\"}]}"));
        using var http = new HttpClient(handler);
        Assert.Equal("existing", await new GoogleDriveClient(http, http).EnsureFolderAsync("token", "KeepClip", "old"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Absent_folder_is_created_with_the_requested_name_and_folder_mime_type()
    {
        using var handler = new Handler(_ => Json("{\"files\":[]}"), _ => Json("{\"id\":\"created\"}"));
        using var http = new HttpClient(handler);
        var name = "KeepClip ' test";

        Assert.Equal("created", await new GoogleDriveClient(http, http).EnsureFolderAsync("token", name, null));

        Assert.Contains("name = 'KeepClip \\' test'", Uri.UnescapeDataString(handler.Requests[0].Uri.Query));
        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, request.Method);
        var metadata = JsonNode.Parse(request.Body)!;
        Assert.Equal(name, (string)metadata["name"]!);
        Assert.Equal("application/vnd.google-apps.folder", (string)metadata["mimeType"]!);
    }

    [Fact]
    public async Task File_listing_follows_pages_and_skips_incomplete_entries()
    {
        using var handler = new Handler(
            _ => Json("{\"nextPageToken\":\"next+/=\",\"files\":[{\"id\":\"one\",\"name\":\"a.mp4\",\"mimeType\":\"video/mp4\",\"size\":\"123\",\"createdTime\":\"2026-10-22T10:00:00Z\",\"appProperties\":{\"game\":\"CS2\",\"ignored\":12}},{\"name\":\"missing-id.mp4\"},{\"id\":\"blank\",\"name\":\" \"}]}"),
            _ => Json("{\"files\":[{\"id\":\"two\",\"name\":\"b.mp4\",\"size\":456,\"createdTime\":\"bad\",\"modifiedTime\":\"2026-10-21T12:00:00Z\"},{\"id\":\"three\",\"name\":\"c.mp4\",\"size\":\"bad\"}]}"));
        using var http = new HttpClient(handler);

        var files = await new GoogleDriveClient(http, http).ListFolderFilesAsync("token", "folder");

        Assert.Equal(new[] { "one", "two", "three" }, files.Select(f => f.Id));
        Assert.Equal(new long[] { 123, 456, 0 }, files.Select(f => f.Size));
        Assert.Equal("CS2", files[0].AppProperties["game"]);
        Assert.Single(files[0].AppProperties);
        Assert.Equal(DateTimeOffset.Parse("2026-10-22T10:00:00Z"), files[0].CreatedTime);
        Assert.Null(files[1].CreatedTime);
        Assert.NotNull(files[1].ModifiedTime);
        Assert.Contains("pageToken=next%2B%2F%3D", handler.Requests[1].Uri.Query);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer token", r.Authorization));
    }

    [Fact]
    public async Task Upload_initializes_metadata_then_streams_the_exact_file_to_the_session()
    {
        var path = Path.Combine(TestEnvironment.NewTempDir("upload"), "test.mp4");
        var bytes = new byte[] { 0, 255, 1, 3 };
        File.WriteAllBytes(path, bytes);
        using var handler = new Handler(_ => { var response = Json("{}"); response.Headers.Location = new Uri("https://www.googleapis.com/upload/session/test"); return response; }, _ => Json("{\"id\":\"uploaded\"}"));
        using var http = new HttpClient(handler);

        var id = await new GoogleDriveClient(http, http).UploadResumableAsync("token", "test.mp4", "folder", path, "video/mp4", new Dictionary<string, string> { ["game"] = "CS2" });

        Assert.Equal("uploaded", id);
        var metadata = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.Equal("folder", (string)metadata["parents"]![0]!);
        Assert.Equal("CS2", (string)metadata["appProperties"]!["game"]!);
        var upload = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, upload.Method);
        Assert.Equal("https://www.googleapis.com/upload/session/test", upload.Uri.AbsoluteUri);
        Assert.Equal("Bearer token", upload.Authorization);
        Assert.Equal(bytes, upload.Body);
        Assert.Equal("video/mp4", upload.ContentType);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Upload_stops_when_the_session_url_is_missing()
    {
        var path = Path.Combine(TestEnvironment.NewTempDir("upload-no-session"), "test.mp4");
        File.WriteAllText(path, "data");
        using var handler = new Handler(_ => Json("{}"));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GoogleDriveClient(http, http).UploadResumableAsync("token", "test.mp4", "folder", path, "video/mp4"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Download_writes_the_response_bytes_and_http_errors_preserve_existing_data()
    {
        var path = Path.Combine(TestEnvironment.NewTempDir("download"), "test.mp4");
        using var handler = new Handler(_ => Image(new byte[] { 3, 2, 1 }, "video/mp4"), _ => Json("{\"error\":\"failed\"}", HttpStatusCode.BadGateway));
        using var http = new HttpClient(handler);
        var client = new GoogleDriveClient(http, http);
        await client.DownloadAsync("token", "remote", path);
        Assert.Equal(new byte[] { 3, 2, 1 }, File.ReadAllBytes(path));
        await Assert.ThrowsAsync<GoogleDrive.GoogleApiException>(() => client.DownloadAsync("token", "remote", path));
        Assert.Equal(new byte[] { 3, 2, 1 }, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("bytes=1-3")]
    [InlineData("invalid range")]
    [InlineData(null)]
    public async Task Media_response_is_returned_to_the_caller_and_cancellation_is_observed(string? range)
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[] { 1, 2 }) });
        using var http = new HttpClient(handler);
        var client = new GoogleDriveClient(http, http);
        using var response = await client.OpenMediaAsync("token", "remote", range, CancellationToken.None);
        Assert.Equal(range == "bytes=1-3" ? range : null, Assert.Single(handler.Requests).Range);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(new byte[] { 1, 2 }, await response.Content.ReadAsByteArrayAsync());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.OpenMediaAsync("token", "remote", range, cts.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Trash_uses_patch_and_preserves_server_failures()
    {
        using var handler = new Handler(_ => Json("{}"), _ => Json("{\"error\":\"denied\"}", HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        var client = new GoogleDriveClient(http, http);
        await client.TrashAsync("token", "remote");
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.True((bool)JsonNode.Parse(request.Body)!["trashed"]!);
        await Assert.ThrowsAsync<GoogleDrive.GoogleApiException>(() => client.TrashAsync("token", "remote"));
    }

    private static HttpResponseMessage Image(byte[] bytes, string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

    private sealed class UnknownLengthStream : MemoryStream
    {
        public int BytesRead;
        public UnknownLengthStream() : base(new byte[5 * 1024 * 1024]) { }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var count = await base.ReadAsync(buffer, ct);
            BytesRead += count;
            return count;
        }
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken ct)
        {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await ReadAsync(buffer.AsMemory(), ct)) > 0)
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    [Fact]
    public async Task Photo_without_content_length_is_stopped_at_the_size_limit()
    {
        using var stream = new UnknownLengthStream();
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GoogleDriveClient(http, http).DownloadProfilePhotoAsync("https://lh3.googleusercontent.com/test"));
        Assert.InRange(stream.BytesRead, 1, 1024 * 1024 + 1);
    }
}