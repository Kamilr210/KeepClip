using System.Text.Json;

namespace KeepClip.Infrastructure;

internal class OAuthBackend
{
    public virtual bool IsConfigured => File.Exists(Config.GoogleClientPath);
    public virtual string? ReadToken() => CredentialStore.Read(Config.DriveTokenTarget);
    public virtual void WriteToken(string token, string? email = null) => CredentialStore.Write(Config.DriveTokenTarget, token, email);
    public virtual void DeleteToken() => CredentialStore.Delete(Config.DriveTokenTarget);
    public virtual Task<GoogleDrive.TokenResult> RefreshAsync(string id, string secret, string refresh) => GoogleDrive.RefreshAsync(id, secret, refresh);
    public virtual Task<GoogleDrive.TokenResult> ExchangeCodeAsync(string id, string secret, string code, string verifier, string redirect) => GoogleDrive.ExchangeCodeAsync(id, secret, code, verifier, redirect);
    public virtual Task<GoogleDrive.AboutInfo> GetAboutAsync(string token) => GoogleDrive.GetAboutAsync(token);
    public virtual Task<GoogleDrive.ProfilePhoto> DownloadPhotoAsync(string url) => GoogleDrive.DownloadProfilePhotoAsync(url);
    public virtual Task RevokeAsync(string token) => GoogleDrive.RevokeAsync(token);

    public virtual (string clientId, string clientSecret) LoadClient()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Config.GoogleClientPath));
        var root = doc.RootElement;

        var creds = root.TryGetProperty("installed", out var ins) ? ins
                  : root.TryGetProperty("web", out var web) ? web
                  : root;
        string id = (creds.TryGetProperty("client_id", out var cid) ? cid.GetString() : null)
            ?? throw new InvalidOperationException(Strings.Get("cloud.missingClientId"));
        string secret = creds.TryGetProperty("client_secret", out var cs) ? cs.GetString() ?? "" : "";
        return (id, secret);
    }
}