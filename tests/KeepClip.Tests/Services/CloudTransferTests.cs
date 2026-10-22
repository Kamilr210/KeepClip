using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KeepClip.Tests.Services;

public class CloudTransferTests
{
    private sealed class Connection : CloudConnection
    {
        public bool Connected = true;
        public Func<Task<string>> Token = () => Task.FromResult("test-token");
        public Func<string, string, string?, Task<string>> Folder = (_, _, _) => Task.FromResult("test-folder");
        public Func<string, string, string, string, string, IReadOnlyDictionary<string, string>, Task<string>> Upload = (_, _, _, _, _, _) => throw new InvalidOperationException("Unexpected upload");
        public Func<string, string, Task> Download = (_, _) => throw new InvalidOperationException("Unexpected download");
        public Func<string, Task> Trash = _ => Task.CompletedTask;
        public Func<Task<List<GoogleDrive.DriveFile>>> Files = () => Task.FromResult(new List<GoogleDrive.DriveFile>());
        public Func<string?, CancellationToken, Task<HttpResponseMessage>> Media = (_, _) => throw new InvalidOperationException("Unexpected media request");
        public int Invalidations;
        public List<string> Recycled = new();
        public override bool IsConnected => Connected;
        public override Task<string> GetAccessTokenAsync() => Token();
        public override Task<string> EnsureFolderAsync(string token, string name, string? knownId) => Folder(token, name, knownId);
        public override Task<string> UploadAsync(string token, string name, string folderId, string path, string mime, IReadOnlyDictionary<string, string> properties) => Upload(token, name, folderId, path, mime, properties);
        public override Task DownloadAsync(string token, string remoteId, string path) => Download(remoteId, path);
        public override Task TrashAsync(string token, string remoteId) => Trash(remoteId);
        public override Task<List<GoogleDrive.DriveFile>> ListFilesAsync(string token, string folderId) => Files();
        public override Task<HttpResponseMessage> OpenMediaAsync(string token, string remoteId, string? range, CancellationToken ct) => Media(range, ct);
        public override string? TrashLocalFile(string path) { Recycled.Add(path); File.Delete(path); return null; }
        public override void InvalidateAbout() => Invalidations++;
    }

    private readonly Connection _connection = new();
    private readonly string _root = TestEnvironment.NewTempDir("cloud-transfer");

    public CloudTransferTests()
    {
        TestDb.Reset();
        TestSettings.Reset();
        Settings.SetClipsRoot(_root);
    }

    private long Clip(string name = "clip.mp4", bool cloud = false)
    {
        var id = TestDb.AddClip(name: name, dir: _root);
        if (cloud) TestDb.Exec("UPDATE clips SET storage='cloud', remote_id='test-remote' WHERE id=$id", ("$id", id));
        else File.WriteAllText(Path.Combine(_root, name), "original local clip");
        return id;
    }

    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.WEBM", "video/webm")]
    [InlineData("clip.mov", "video/quicktime")]
    [InlineData("clip.mkv", "video/x-matroska")]
    [InlineData("clip.avi", "video/x-msvideo")]
    [InlineData("clip.other", "video/mp4")]
    public async Task Upload_commits_remote_metadata_before_recycling_the_local_file(string name, string mime)
    {
        var id = Clip(name);
        TestDb.Exec("UPDATE clips SET duration=12.5 WHERE id=$id", ("$id", id));
        _connection.Upload = (token, filename, folder, path, contentType, properties) =>
        {
            Assert.Equal("test-token", token);
            Assert.Equal(name, filename);
            Assert.Equal("test-folder", folder);
            Assert.Equal(mime, contentType);
            Assert.Equal("original local clip", File.ReadAllText(path));
            Assert.Equal("12.5", properties["duration"]);
            Assert.Equal("1", properties["keepclip"]);
            return Task.FromResult("uploaded-id");
        };
        Assert.False(await CloudService.UploadClipAsync(id, _connection));
        var clip = new ClipRepository().GetById(id)!;
        Assert.Equal("cloud", clip.Storage);
        Assert.Equal("uploaded-id", clip.RemoteId);
        Assert.False(File.Exists(clip.Filepath));
        Assert.Single(_connection.Recycled);
        Assert.Equal(1, _connection.Invalidations);
        Assert.Equal("test-folder", Settings.GetString("cloud_folder_id"));
    }

    [Fact]
    public async Task Failed_upload_preserves_the_original_and_local_database_state()
    {
        var id = Clip();
        _connection.Upload = (_, _, _, _, _, _) => Task.FromException<string>(new IOException("offline"));
        await Assert.ThrowsAsync<IOException>(() => CloudService.UploadClipAsync(id, _connection));
        var clip = new ClipRepository().GetById(id)!;
        Assert.Equal("local", clip.Storage);
        Assert.Null(clip.RemoteId);
        Assert.Equal("original local clip", File.ReadAllText(clip.Filepath!));
        Assert.Empty(_connection.Recycled);
        Assert.Equal(0, _connection.Invalidations);
    }

    [Fact]
    public async Task Operations_already_completed_are_skipped_without_authentication()
    {
        var local = Clip();
        var remote = Clip("remote.mp4", true);
        _connection.Token = () => throw new InvalidOperationException("Must not authenticate");
        Assert.True(await CloudService.UploadClipAsync(remote, _connection));
        Assert.True(await CloudService.DownloadClipAsync(local, _connection));
    }

    [Fact]
    public async Task Missing_clips_or_files_are_rejected_before_authentication()
    {
        _connection.Token = () => throw new InvalidOperationException("Must not authenticate");
        await Assert.ThrowsAsync<FileNotFoundException>(() => CloudService.UploadClipAsync(999, _connection));
        await Assert.ThrowsAsync<FileNotFoundException>(() => CloudService.DownloadClipAsync(999, _connection));
        var missing = TestDb.AddClip(dir: _root);
        await Assert.ThrowsAsync<FileNotFoundException>(() => CloudService.UploadClipAsync(missing, _connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_publishes_complete_data_before_recycling_remote_copy(bool trashFails)
    {
        var id = Clip(cloud: true);
        var path = new ClipRepository().GetById(id)!.Filepath!;
        _connection.Download = (remote, temporary) =>
        {
            Assert.Equal("test-remote", remote);
            Assert.NotEqual(path, temporary);
            File.WriteAllText(temporary, "downloaded data");
            return Task.CompletedTask;
        };
        _connection.Trash = remote =>
        {
            Assert.Equal("downloaded data", File.ReadAllText(path));
            Assert.Equal("local", new ClipRepository().GetById(id)!.Storage);
            return trashFails ? Task.FromException(new IOException("cleanup failed")) : Task.CompletedTask;
        };
        Assert.False(await CloudService.DownloadClipAsync(id, _connection));
        Assert.Equal("downloaded data", File.ReadAllText(path));
        Assert.Null(new ClipRepository().GetById(id)!.RemoteId);
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal(1, _connection.Invalidations);
    }

    [Fact]
    public async Task Interrupted_download_discards_partial_data_and_retains_the_remote_reference()
    {
        var id = Clip(cloud: true);
        var path = new ClipRepository().GetById(id)!.Filepath!;
        File.WriteAllText(path, "existing local data");
        _connection.Download = (_, temporary) => { File.WriteAllText(temporary, "partial"); throw new IOException("connection lost"); };
        _connection.Trash = _ => throw new InvalidOperationException("Must not trash remote copy");

        await Assert.ThrowsAsync<IOException>(() => CloudService.DownloadClipAsync(id, _connection));
        Assert.Equal("existing local data", File.ReadAllText(path));
        Assert.Equal("cloud", new ClipRepository().GetById(id)!.Storage);
        Assert.Equal("test-remote", new ClipRepository().GetById(id)!.RemoteId);
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Invalid_download_metadata_is_rejected_before_connecting()
    {
        var id = Clip(cloud: true);
        _connection.Token = () => throw new InvalidOperationException("Must not connect");
        TestDb.Exec("UPDATE clips SET remote_id=NULL WHERE id=$id", ("$id", id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CloudService.DownloadClipAsync(id, _connection));
        TestDb.Exec("UPDATE clips SET filepath='' WHERE id=$id", ("$id", id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CloudService.DownloadClipAsync(id, _connection));
    }

    [Fact]
    public async Task Folder_upload_counts_successes_skips_and_failures_independently()
    {
        var good = Clip("good.mp4");
        var bad = Clip("bad.mp4");
        var remote = Clip("remote.mp4", true);
        TestDb.Exec("INSERT INTO folders(id,name) VALUES(123,'test'); INSERT INTO folder_clips(folder_id,clip_id) VALUES(123,$a),(123,$b),(123,$c)", ("$a", good), ("$b", bad), ("$c", remote));
        _connection.Upload = (_, name, _, _, _, _) => name == "bad.mp4" ? Task.FromException<string>(new IOException("offline")) : Task.FromResult("uploaded");
        Assert.Equal((1, 3, 1, 1), await CloudService.UploadFolderAsync(123, _connection));
        Assert.Equal((0, 0, 0, 0), await CloudService.UploadFolderAsync(999, _connection));
    }

    [Fact]
    public async Task Failed_sync_releases_the_gate_for_the_next_attempt()
    {
        _connection.Files = () => Task.FromException<List<GoogleDrive.DriveFile>>(new IOException("offline"));
        await Assert.ThrowsAsync<IOException>(() => CloudService.SyncFromCloudAsync(_connection));
        _connection.Files = () => Task.FromResult(new List<GoogleDrive.DriveFile>());
        Assert.Equal(new CloudService.SyncResult(0, 0, 0, 0), await CloudService.SyncFromCloudAsync(_connection).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Remote_trash_requires_a_connection_and_invalidates_quota_only_after_success()
    {
        _connection.Connected = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CloudService.TrashRemoteAsync("id", _connection));
        Assert.Equal(0, _connection.Invalidations);
        _connection.Connected = true;
        await CloudService.TrashRemoteAsync("id", _connection);
        Assert.Equal(1, _connection.Invalidations);
    }

    [Fact]
    public async Task Proxy_forwards_ranges_status_headers_and_exact_content()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Range = "bytes=1-3";
        http.Response.Body = new MemoryStream();
        _connection.Media = (range, _) =>
        {
            Assert.Equal("bytes=1-3", range);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(1, 3, 10);
            return Task.FromResult(response);
        };
        await CloudService.ProxyVideoAsync(http, "remote", _connection);
        Assert.Equal(206, http.Response.StatusCode);
        Assert.Equal("video/mp4", http.Response.ContentType);
        Assert.Equal("bytes 1-3/10", http.Response.Headers.ContentRange.ToString());
        Assert.Equal("bytes", http.Response.Headers.AcceptRanges.ToString());
        Assert.Equal(new byte[] { 1, 2, 3 }, ((MemoryStream)http.Response.Body).ToArray());
    }

    [Fact]
    public async Task Proxy_reports_authentication_failure_without_requesting_remote_media()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        _connection.Token = () => Task.FromException<string>(new IOException("not connected"));
        await CloudService.ProxyVideoAsync(http, "remote", _connection);
        Assert.Equal(502, http.Response.StatusCode);
        Assert.Contains("not connected", System.Text.Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()));
    }
}