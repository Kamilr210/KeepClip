namespace KeepClip.Infrastructure;

public static class GoogleDrive
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string AuthEndpoint  = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    public const string Scope = "https://www.googleapis.com/auth/drive.file";

    private static readonly HttpClient Api = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly HttpClient Transfer = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly GoogleDriveClient Client = new(Api, Transfer);

    public readonly record struct TokenResult(string AccessToken, string? RefreshToken, int ExpiresInSeconds);
    public readonly record struct AboutInfo(string? Name, string? Email, string? Photo, long? Usage, long? Limit);
    public readonly record struct ProfilePhoto(byte[] Data, string ContentType);
    public sealed record DriveFile(
        string Id,
        string Name,
        string MimeType,
        long Size,
        DateTimeOffset? CreatedTime,
        DateTimeOffset? ModifiedTime,
        IReadOnlyDictionary<string, string> AppProperties);

    public static Task<TokenResult> ExchangeCodeAsync(string clientId, string clientSecret, string code, string codeVerifier, string redirectUri)
        => Client.ExchangeCodeAsync(clientId, clientSecret, code, codeVerifier, redirectUri);

    public static Task<TokenResult> RefreshAsync(string clientId, string clientSecret, string refreshToken)
        => Client.RefreshAsync(clientId, clientSecret, refreshToken);

    public static Task RevokeAsync(string token) => Client.RevokeAsync(token);
    public static Task<AboutInfo> GetAboutAsync(string accessToken) => Client.GetAboutAsync(accessToken);
    public static Task<ProfilePhoto> DownloadProfilePhotoAsync(string photoUrl) => Client.DownloadProfilePhotoAsync(photoUrl);
    public static Task<string> EnsureFolderAsync(string accessToken, string folderName, string? knownId) => Client.EnsureFolderAsync(accessToken, folderName, knownId);

    public static Task<string> UploadResumableAsync(string accessToken, string name, string parentId, string filePath, string mimeType, IReadOnlyDictionary<string, string>? appProperties = null)
        => Client.UploadResumableAsync(accessToken, name, parentId, filePath, mimeType, appProperties);

    public static Task<List<DriveFile>> ListFolderFilesAsync(string accessToken, string folderId) => Client.ListFolderFilesAsync(accessToken, folderId);
    public static Task DownloadAsync(string accessToken, string fileId, string destPath) => Client.DownloadAsync(accessToken, fileId, destPath);
    public static Task<HttpResponseMessage> OpenMediaAsync(string accessToken, string fileId, string? range, CancellationToken ct) => Client.OpenMediaAsync(accessToken, fileId, range, ct);
    public static Task TrashAsync(string accessToken, string fileId) => Client.TrashAsync(accessToken, fileId);

    public sealed class GoogleApiException(string message) : Exception(message)
    {
        public bool IsInvalidGrant =>
            Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("Token has been expired or revoked", StringComparison.OrdinalIgnoreCase);
    }
}
