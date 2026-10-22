namespace KeepClip.Services;

internal class CloudConnection
{
    public virtual bool IsConnected => OAuthService.IsConfigured() && OAuthService.IsConnected();
    public virtual Task<string> GetAccessTokenAsync() => OAuthService.GetAccessTokenAsync();
    public virtual Task<string> EnsureFolderAsync(string token, string name, string? knownId) => GoogleDrive.EnsureFolderAsync(token, name, knownId);
    public virtual Task<List<GoogleDrive.DriveFile>> ListFilesAsync(string token, string folderId) => GoogleDrive.ListFolderFilesAsync(token, folderId);
    public virtual Task<string> UploadAsync(string token, string name, string folderId, string path, string mime, IReadOnlyDictionary<string, string> properties) => GoogleDrive.UploadResumableAsync(token, name, folderId, path, mime, properties);
    public virtual Task DownloadAsync(string token, string remoteId, string path) => GoogleDrive.DownloadAsync(token, remoteId, path);
    public virtual Task TrashAsync(string token, string remoteId) => GoogleDrive.TrashAsync(token, remoteId);
    public virtual Task<HttpResponseMessage> OpenMediaAsync(string token, string remoteId, string? range, CancellationToken ct) => GoogleDrive.OpenMediaAsync(token, remoteId, range, ct);
    public virtual string? TrashLocalFile(string path) => Trash.SendWithRetry(path);
    public virtual void InvalidateAbout() => OAuthService.InvalidateAbout();
}