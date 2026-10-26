using System.Text.Json;

namespace KeepClip.Infrastructure;

internal class OAuthBackend
{
    public virtual bool IsConfigured => File.Exists(Config.GoogleClientPath);
    public virtual string? ReadToken() => CredentialStore.Read(Config.DriveTokenTarget);
    public virtual void WriteToken(string token, string? email = null) => CredentialStore.Write(Config.DriveTokenTarget, token, email);
    public virtual void DeleteToken() => CredentialStore.Delete(Config.DriveTokenTarget);
    public virtual Task<GoogleDrive.TokenResult> RefreshAsync(string id, string refresh) => GoogleDrive.RefreshAsync(id, refresh);
    public virtual Task<GoogleDrive.TokenResult> ExchangeCodeAsync(string id, string code, string verifier, string redirect) => GoogleDrive.ExchangeCodeAsync(id, code, verifier, redirect);
    public virtual Task<GoogleDrive.AboutInfo> GetAboutAsync(string token) => GoogleDrive.GetAboutAsync(token);
    public virtual Task<GoogleDrive.ProfilePhoto> DownloadPhotoAsync(string url) => GoogleDrive.DownloadProfilePhotoAsync(url);
    public virtual Task RevokeAsync(string token) => GoogleDrive.RevokeAsync(token);

    public virtual string LoadClientId()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Config.GoogleClientPath));
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("web", out _) || !root.TryGetProperty("installed", out var installed) || installed.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("KeepClip wymaga konfiguracji OAuth typu Desktop app (installed).");
        if (!installed.TryGetProperty("client_id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() is not { Length: > 0 and <= 1024 } value || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
            throw new InvalidOperationException(Strings.Get("cloud.missingClientId"));
        return value;
    }
}