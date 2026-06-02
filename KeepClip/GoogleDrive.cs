using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KeepClip;

/// <summary>
/// Thin REST client for Google's OAuth token endpoint and the Drive v3 API. Hand-rolled
/// over <see cref="HttpClient"/> (no Google.Apis SDK) to keep the install small and the
/// dependency surface tiny, matching the rest of the project. All calls take an access
/// token; minting/refreshing tokens is <see cref="ExchangeCodeAsync"/> / <see cref="RefreshAsync"/>.
///
/// Only the <c>drive.file</c> scope is used, so the app sees <b>only the files it creates</b> —
/// never the rest of the user's Drive. Account name/e-mail/photo and the storage quota both
/// come from <c>drive.about.get</c>, so no extra <c>userinfo</c>/<c>openid</c> scope is needed.
/// </summary>
public static class GoogleDrive
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string AuthEndpoint  = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    public const string Scope = "https://www.googleapis.com/auth/drive.file";

    // Short-timeout client for JSON/control calls; infinite-timeout client for the
    // potentially long upload/download of multi-GB clips (HttpClient.Timeout is the
    // whole-operation budget, so a fixed value would abort big transfers).
    private static readonly HttpClient Api = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly HttpClient Transfer = new() { Timeout = Timeout.InfiniteTimeSpan };

    public readonly record struct TokenResult(string AccessToken, string? RefreshToken, int ExpiresInSeconds);
    public readonly record struct AboutInfo(string? Name, string? Email, string? Photo, long? Usage, long? Limit);

    // ---- OAuth ----

    /// <summary>Exchange an authorization code (+ PKCE verifier) for access/refresh tokens.</summary>
    public static async Task<TokenResult> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string codeVerifier, string redirectUri)
    {
        using var resp = await Api.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        }));
        var json = await ReadJsonOrThrow(resp);
        return new TokenResult(
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600);
    }

    /// <summary>Mint a fresh access token from a stored refresh token. Refresh tokens are
    /// reusable, so the result's <c>RefreshToken</c> is usually null (keep the old one).</summary>
    public static async Task<TokenResult> RefreshAsync(string clientId, string clientSecret, string refreshToken)
    {
        using var resp = await Api.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }));
        var json = await ReadJsonOrThrow(resp);
        return new TokenResult(
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600);
    }

    /// <summary>Best-effort revoke of a refresh/access token on disconnect.</summary>
    public static async Task RevokeAsync(string token)
    {
        try
        {
            using var _ = await Api.PostAsync(RevokeEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        }
        catch { /* revoke is courtesy; local token is cleared regardless */ }
    }

    // ---- Drive ----

    /// <summary>Account identity + storage quota in one call (needs only drive.file).</summary>
    public static async Task<AboutInfo> GetAboutAsync(string accessToken)
    {
        using var req = Authorized(HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress,photoLink),storageQuota(limit,usage)",
            accessToken);
        using var resp = await Api.SendAsync(req);
        var json = await ReadJsonOrThrow(resp);

        string? name = null, email = null, photo = null;
        if (json.TryGetProperty("user", out var u))
        {
            name = u.TryGetProperty("displayName", out var n) ? n.GetString() : null;
            email = u.TryGetProperty("emailAddress", out var e) ? e.GetString() : null;
            photo = u.TryGetProperty("photoLink", out var p) ? p.GetString() : null;
        }
        long? usage = null, limit = null;
        if (json.TryGetProperty("storageQuota", out var q))
        {
            if (q.TryGetProperty("usage", out var us) && long.TryParse(us.GetString(), out var uv)) usage = uv;
            if (q.TryGetProperty("limit", out var li) && long.TryParse(li.GetString(), out var lv)) limit = lv;
        }
        return new AboutInfo(name, email, photo, usage, limit);
    }

    /// <summary>
    /// Resolve the app's "KeepClip" Drive folder, creating it if needed. Tries the
    /// remembered id first, then a name search among app-created folders, then creates one.
    /// Returns the folder id (persist it via the caller).
    /// </summary>
    public static async Task<string> EnsureFolderAsync(string accessToken, string folderName, string? knownId)
    {
        // 1) Remembered id still valid?
        if (!string.IsNullOrEmpty(knownId))
        {
            using var req = Authorized(HttpMethod.Get,
                $"https://www.googleapis.com/drive/v3/files/{knownId}?fields=id,trashed", accessToken);
            using var resp = await Api.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var j = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
                if (!(j.TryGetProperty("trashed", out var tr) && tr.GetBoolean()))
                    return j.GetProperty("id").GetString()!;
            }
        }

        // 2) Search app-created folders by name.
        string q = Uri.EscapeDataString(
            $"mimeType = 'application/vnd.google-apps.folder' and name = '{folderName.Replace("'", "\\'")}' and trashed = false");
        using (var sreq = Authorized(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files?q={q}&spaces=drive&fields=files(id,name)", accessToken))
        using (var sresp = await Api.SendAsync(sreq))
        {
            var sj = await ReadJsonOrThrow(sresp);
            if (sj.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                return files[0].GetProperty("id").GetString()!;
        }

        // 3) Create it.
        var meta = JsonSerializer.Serialize(new { name = folderName, mimeType = "application/vnd.google-apps.folder" });
        using var creq = Authorized(HttpMethod.Post,
            "https://www.googleapis.com/drive/v3/files?fields=id", accessToken);
        creq.Content = new StringContent(meta, Encoding.UTF8, "application/json");
        using var cresp = await Api.SendAsync(creq);
        var cj = await ReadJsonOrThrow(cresp);
        return cj.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Resumable upload of a file into <paramref name="parentId"/>. The bytes are streamed
    /// (never fully buffered) so multi-GB clips don't blow up memory. Returns the new file id.
    /// </summary>
    public static async Task<string> UploadResumableAsync(
        string accessToken, string name, string parentId, string filePath, string mimeType)
    {
        long length = new FileInfo(filePath).Length;

        // 1) Initiate the session.
        using var init = Authorized(HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id", accessToken);
        init.Headers.Add("X-Upload-Content-Type", mimeType);
        init.Headers.Add("X-Upload-Content-Length", length.ToString());
        var meta = JsonSerializer.Serialize(new { name, parents = new[] { parentId } });
        init.Content = new StringContent(meta, Encoding.UTF8, "application/json");
        using var initResp = await Api.SendAsync(init);
        if (!initResp.IsSuccessStatusCode) await ThrowFrom(initResp);
        var session = initResp.Headers.Location
            ?? throw new InvalidOperationException("Drive nie zwrócił adresu sesji uploadu.");

        // 2) Upload the bytes in one streamed PUT.
        using var fs = File.OpenRead(filePath);
        using var put = new HttpRequestMessage(HttpMethod.Put, session);
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var body = new StreamContent(fs);
        body.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        body.Headers.ContentLength = length;
        put.Content = body;
        using var putResp = await Transfer.SendAsync(put);
        var pj = await ReadJsonOrThrow(putResp);
        return pj.GetProperty("id").GetString()!;
    }

    /// <summary>Stream a Drive file's bytes to <paramref name="destPath"/>.</summary>
    public static async Task DownloadAsync(string accessToken, string fileId, string destPath)
    {
        using var req = Authorized(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media", accessToken);
        using var resp = await Transfer.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode) await ThrowFrom(resp);
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst);
    }

    /// <summary>
    /// Open a Drive file's media stream for proxying to the browser's <c>&lt;video&gt;</c>
    /// element, forwarding an optional HTTP <c>Range</c> header so seeking works (Drive's
    /// <c>alt=media</c> endpoint honours ranges and replies 206 with Content-Range). The
    /// caller owns the returned response: relay its status/headers/body, then dispose it.
    /// </summary>
    public static async Task<HttpResponseMessage> OpenMediaAsync(
        string accessToken, string fileId, string? range, CancellationToken ct)
    {
        var req = Authorized(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media", accessToken);
        if (!string.IsNullOrEmpty(range) && RangeHeaderValue.TryParse(range, out var rh))
            req.Headers.Range = rh;
        return await Transfer.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>Move a Drive file to the Drive trash (recoverable ~30 days, like the Recycle Bin).</summary>
    public static async Task TrashAsync(string accessToken, string fileId)
    {
        using var req = Authorized(HttpMethod.Patch,
            $"https://www.googleapis.com/drive/v3/files/{fileId}", accessToken);
        req.Content = new StringContent("{\"trashed\":true}", Encoding.UTF8, "application/json");
        using var resp = await Api.SendAsync(req);
        if (!resp.IsSuccessStatusCode) await ThrowFrom(resp);
    }

    // ---- helpers ----

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return req;
    }

    private static async Task<JsonElement> ReadJsonOrThrow(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new GoogleApiException(ExtractError(text, resp));
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ThrowFrom(HttpResponseMessage resp)
        => throw new GoogleApiException(ExtractError(await resp.Content.ReadAsStringAsync(), resp));

    /// <summary>Pull a human-usable message out of Google's error JSON (token endpoint uses
    /// <c>error_description</c>; the Drive API uses <c>error.message</c>).</summary>
    private static string ExtractError(string body, HttpResponseMessage resp)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.TryGetProperty("error_description", out var ed)) return ed.GetString() ?? body;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? body;
                if (err.TryGetProperty("message", out var m)) return m.GetString() ?? body;
            }
        }
        catch { /* not JSON */ }
        return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
    }

    /// <summary>Carries whether the failure means the grant is dead (token revoked/expired),
    /// so the caller can flip to "disconnected" instead of just surfacing an error.</summary>
    public sealed class GoogleApiException(string message) : Exception(message)
    {
        public bool IsInvalidGrant =>
            Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("Token has been expired or revoked", StringComparison.OrdinalIgnoreCase);
    }
}
