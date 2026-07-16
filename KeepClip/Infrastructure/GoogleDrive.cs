using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KeepClip.Infrastructure;

// Zakres uprawnień ogranicza dostęp do plików utworzonych przez aplikację.
public static class GoogleDrive
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string AuthEndpoint  = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    public const string Scope = "https://www.googleapis.com/auth/drive.file";

    // Transfery wielogigabajtowych klipów nie mogą mieć limitu czasu dla całej operacji,
    // dlatego używają osobnego klienta HTTP bez takiego ograniczenia.
    private static readonly HttpClient Api = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly HttpClient Transfer = new() { Timeout = Timeout.InfiniteTimeSpan };

    public readonly record struct TokenResult(string AccessToken, string? RefreshToken, int ExpiresInSeconds);
    public readonly record struct AboutInfo(string? Name, string? Email, string? Photo, long? Usage, long? Limit);

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

    public static async Task RevokeAsync(string token)
    {
        try
        {
            using var _ = await Api.PostAsync(RevokeEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        }
        catch { }
    }

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

    public static async Task<string> EnsureFolderAsync(string accessToken, string folderName, string? knownId)
    {
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

        var meta = JsonSerializer.Serialize(new { name = folderName, mimeType = "application/vnd.google-apps.folder" });
        using var creq = Authorized(HttpMethod.Post,
            "https://www.googleapis.com/drive/v3/files?fields=id", accessToken);
        creq.Content = new StringContent(meta, Encoding.UTF8, "application/json");
        using var cresp = await Api.SendAsync(creq);
        var cj = await ReadJsonOrThrow(cresp);
        return cj.GetProperty("id").GetString()!;
    }

    // Dane są strumieniowane, aby duży klip nie był buforowany w pamięci.
    public static async Task<string> UploadResumableAsync(
        string accessToken, string name, string parentId, string filePath, string mimeType)
    {
        long length = new FileInfo(filePath).Length;

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

    // Nagłówek HTTP Range jest przekazywany do Dysku Google, aby umożliwić przewijanie.
    public static async Task<HttpResponseMessage> OpenMediaAsync(
        string accessToken, string fileId, string? range, CancellationToken ct)
    {
        var req = Authorized(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media", accessToken);
        if (!string.IsNullOrEmpty(range) && RangeHeaderValue.TryParse(range, out var rh))
            req.Headers.Range = rh;
        return await Transfer.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public static async Task TrashAsync(string accessToken, string fileId)
    {
        using var req = Authorized(HttpMethod.Patch,
            $"https://www.googleapis.com/drive/v3/files/{fileId}", accessToken);
        req.Content = new StringContent("{\"trashed\":true}", Encoding.UTF8, "application/json");
        using var resp = await Api.SendAsync(req);
        if (!resp.IsSuccessStatusCode) await ThrowFrom(resp);
    }


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
        catch { }
        return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
    }

    public sealed class GoogleApiException(string message) : Exception(message)
    {
        public bool IsInvalidGrant =>
            Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("Token has been expired or revoked", StringComparison.OrdinalIgnoreCase);
    }
}
