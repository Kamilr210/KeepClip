using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeepClip.Infrastructure;

/// <summary>
/// Google OAuth 2.0 lifecycle for the Drive offload: PKCE loopback connect, access-token
/// cache/refresh, account/quota status, and disconnect. Owns ALL auth state — <see cref="CloudService"/>
/// sits on top of this and only calls its public/internal methods for the actual
/// upload/download/proxy work.
///
/// "Aternos-style single client": the OAuth <i>app</i> credentials come from one gitignored
/// file (<see cref="Config.GoogleClientPath"/>) the author embeds in shipped builds — no
/// per-user setup. The per-user <i>refresh token</i> lives in the Windows Credential Manager
/// (<see cref="CredentialStore"/>), never in plaintext. Single account, single process; all
/// state below is static.
/// </summary>
public static class OAuthService
{
    // Access-token cache. Refresh tokens are long-lived; access tokens last ~1 h, so we
    // mint one and reuse it until just before expiry. SemaphoreSlim (not lock) because the
    // refresh call is async.
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _accessExpiry = DateTimeOffset.MinValue;

    // Account/quota cache so the frontend's frequent /api/cloud/status polls don't hammer
    // Drive. Short TTL keeps the quota bar reasonably fresh.
    private static readonly object AboutGate = new();
    private static GoogleDrive.AboutInfo? _about;
    private static DateTimeOffset _aboutAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan AboutTtl = TimeSpan.FromSeconds(20);
    private static string? _lastError;

    // In-flight OAuth connect (loopback listener + PKCE state), at most one at a time.
    private static readonly object ConnectGate = new();
    private static HttpListener? _listener;
    private static CancellationTokenSource? _connectCts;

    // ---- configuration / connection state ----

    /// <summary>True once the author/user has dropped <c>data/google_client.json</c>.</summary>
    public static bool IsConfigured() => File.Exists(Config.GoogleClientPath);

    /// <summary>True when a refresh token is stored (i.e. an account has been linked).</summary>
    public static bool IsConnected() =>
        !string.IsNullOrEmpty(CredentialStore.Read(Config.DriveTokenTarget));

    private static (string clientId, string clientSecret) LoadClient()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Config.GoogleClientPath));
        var root = doc.RootElement;
        // Google's downloaded JSON wraps the creds in "installed" (Desktop app) or "web".
        var creds = root.TryGetProperty("installed", out var ins) ? ins
                  : root.TryGetProperty("web", out var web) ? web
                  : root;
        string id = (creds.TryGetProperty("client_id", out var cid) ? cid.GetString() : null)
            ?? throw new InvalidOperationException("Brak client_id w google_client.json.");
        string secret = creds.TryGetProperty("client_secret", out var cs) ? cs.GetString() ?? "" : "";
        return (id, secret);
    }

    // ---- status ----

    /// <summary>
    /// Assemble the payload the Cloud view polls: <c>configured</c>, <c>connected</c>, and
    /// (when connected) the account identity + storage quota. Keys match the frontend
    /// verbatim (name/email/photo/usage/limit/error).
    /// </summary>
    public static async Task<Dictionary<string, object?>> StatusAsync()
    {
        var st = new Dictionary<string, object?>
        {
            ["configured"] = IsConfigured(),
            ["connected"] = false,
        };
        if (!IsConfigured() || !IsConnected()) return st;

        try
        {
            var token = await GetAccessTokenAsync();
            var about = await GetAboutAsync(token);
            st["connected"] = true;
            st["name"] = about.Name;
            st["email"] = about.Email;
            st["photo"] = about.Photo;
            st["usage"] = about.Usage;
            st["limit"] = about.Limit;
            if (_lastError != null) st["error"] = _lastError;
        }
        catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
        {
            // Grant is dead (revoked/expired) → drop the token and fall back to "connect".
            DisconnectLocal();
            st["connected"] = false;
        }
        catch (Exception ex)
        {
            // Transient (network/Drive blip) — still linked: surface the error and reuse
            // the last known account/quota so the panel doesn't flicker empty.
            _lastError = ex.Message;
            st["connected"] = true;
            st["error"] = ex.Message;
            lock (AboutGate)
            {
                if (_about is { } a)
                {
                    st["name"] = a.Name; st["email"] = a.Email; st["photo"] = a.Photo;
                    st["usage"] = a.Usage; st["limit"] = a.Limit;
                }
            }
        }
        return st;
    }

    // ---- tokens ----

    /// <summary>A valid access token, refreshed if the cached one is near expiry. Used by
    /// <see cref="CloudService"/> for every Drive call.</summary>
    internal static async Task<string> GetAccessTokenAsync()
    {
        await TokenGate.WaitAsync();
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _accessExpiry)
                return _accessToken;

            var refresh = CredentialStore.Read(Config.DriveTokenTarget)
                ?? throw new InvalidOperationException("Nie połączono z Google Drive.");
            var (id, secret) = LoadClient();
            try
            {
                var tok = await GoogleDrive.RefreshAsync(id, secret, refresh);
                _accessToken = tok.AccessToken;
                // Renew a minute early to avoid races near the boundary.
                _accessExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tok.ExpiresInSeconds - 60));
                if (!string.IsNullOrEmpty(tok.RefreshToken)) // Google rotates rarely; keep up to date if so
                    CredentialStore.Write(Config.DriveTokenTarget, tok.RefreshToken!);
                return _accessToken;
            }
            catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
            {
                DisconnectLocal();
                throw;
            }
        }
        finally { TokenGate.Release(); }
    }

    private static async Task<GoogleDrive.AboutInfo> GetAboutAsync(string token)
    {
        lock (AboutGate)
        {
            if (_about is { } cached && DateTimeOffset.UtcNow - _aboutAt < AboutTtl)
                return cached;
        }
        var about = await GoogleDrive.GetAboutAsync(token);
        lock (AboutGate) { _about = about; _aboutAt = DateTimeOffset.UtcNow; _lastError = null; }
        return about;
    }

    /// <summary>Force the next status poll to re-fetch the quota (usage changed).</summary>
    internal static void InvalidateAbout() { lock (AboutGate) { _aboutAt = DateTimeOffset.MinValue; } }

    // ---- connect (OAuth 2.0 PKCE, loopback redirect) ----

    /// <summary>
    /// Start an OAuth connect: spin up a one-shot loopback listener, build the Google
    /// consent URL (PKCE, offline access) and return it. The frontend opens the URL in a
    /// browser tab and polls <see cref="StatusAsync"/>; when Google redirects back, the
    /// listener exchanges the code and stores the refresh token, flipping "connected" to
    /// true on the next poll.
    /// </summary>
    public static string BeginConnect()
    {
        var (clientId, _) = LoadClient(); // validate config up front

        int port = FreeLoopbackPort();
        string redirectUri = $"http://127.0.0.1:{port}/";
        string verifier = RandomUrlToken(64);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = RandomUrlToken(24);

        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // matches the frontend's poll cap
        lock (ConnectGate)
        {
            // Replace any previous in-flight attempt.
            _connectCts?.Cancel();
            try { _listener?.Close(); } catch { /* already closed */ }
            _listener = listener;
            _connectCts = cts;
        }

        _ = Task.Run(() => CaptureRedirectAsync(listener, clientId, verifier, state, redirectUri, cts.Token));

        return $"{GoogleDrive.AuthEndpoint}?" + string.Join("&", new[]
        {
            "client_id="            + Uri.EscapeDataString(clientId),
            "redirect_uri="         + Uri.EscapeDataString(redirectUri),
            "response_type=code",
            "scope="                + Uri.EscapeDataString(GoogleDrive.Scope),
            "code_challenge="       + challenge,
            "code_challenge_method=S256",
            "state="                + state,
            "access_type=offline",  // ask for a refresh token
            "prompt=consent",       // force its issuance even on re-consent
        });
    }

    private static async Task CaptureRedirectAsync(
        HttpListener listener, string clientId, string verifier, string expectedState,
        string redirectUri, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var ctxTask = listener.GetContextAsync();
                var done = await Task.WhenAny(ctxTask, Task.Delay(Timeout.Infinite, ct));
                if (done != ctxTask) break; // cancelled / timed out
                var ctx = await ctxTask;

                var q = ctx.Request.QueryString;
                string? code = q["code"];
                string? state = q["state"];
                string? error = q["error"];

                // Stray requests (e.g. the browser's favicon probe) — answer and keep waiting.
                if (code is null && error is null) { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }

                if (error is null && code is not null && state == expectedState)
                {
                    try
                    {
                        var (_, secret) = LoadClient();
                        var tok = await GoogleDrive.ExchangeCodeAsync(clientId, secret, code, verifier, redirectUri);
                        if (string.IsNullOrEmpty(tok.RefreshToken))
                            throw new InvalidOperationException("Google nie zwrócił tokenu odświeżania.");

                        // Fetch identity to label the credential entry and warm the cache.
                        string? email = null;
                        try
                        {
                            var about = await GoogleDrive.GetAboutAsync(tok.AccessToken);
                            email = about.Email;
                            lock (AboutGate) { _about = about; _aboutAt = DateTimeOffset.UtcNow; }
                        }
                        catch { /* identity is cosmetic; the token is what matters */ }

                        CredentialStore.Write(Config.DriveTokenTarget, tok.RefreshToken!, email);
                        await TokenGate.WaitAsync(ct);
                        try
                        {
                            _accessToken = tok.AccessToken;
                            _accessExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tok.ExpiresInSeconds - 60));
                        }
                        finally { TokenGate.Release(); }
                        _lastError = null;

                        WriteHtml(ctx, "Połączono z Google Drive", "Możesz zamknąć tę kartę i wrócić do KeepClip.");
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        WriteHtml(ctx, "Nie udało się połączyć", ex.Message);
                    }
                }
                else
                {
                    WriteHtml(ctx, "Logowanie anulowane", error ?? "Brak kodu autoryzacji.");
                }
                break; // one-shot: a real redirect (success or denial) ends the attempt
            }
        }
        catch (Exception ex) { _lastError = ex.Message; }
        finally
        {
            try { listener.Close(); } catch { /* already closed */ }
            lock (ConnectGate) { if (_listener == listener) { _listener = null; _connectCts = null; } }
        }
    }

    private static void WriteHtml(HttpListenerContext ctx, string title, string body)
    {
        string html =
            "<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\"><title>KeepClip</title>" +
            "<style>body{font-family:system-ui,Segoe UI,sans-serif;background:#070a10;color:#e6e9f0;" +
            "display:grid;place-items:center;height:100vh;margin:0}div{text-align:center;max-width:420px;" +
            "padding:24px}h1{font-size:20px;margin:0 0 8px}p{color:#8b92a5;line-height:1.5}</style></head>" +
            $"<body><div><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(body)}</p></div></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        try
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch { /* client closed the tab */ }
    }

    // ---- disconnect ----

    /// <summary>Revoke the grant with Google (best-effort) and clear all local state.</summary>
    public static async Task DisconnectAsync()
    {
        var refresh = CredentialStore.Read(Config.DriveTokenTarget);
        DisconnectLocal();
        if (!string.IsNullOrEmpty(refresh))
            await GoogleDrive.RevokeAsync(refresh!);
    }

    private static void DisconnectLocal()
    {
        CredentialStore.Delete(Config.DriveTokenTarget);
        _accessToken = null;
        _accessExpiry = DateTimeOffset.MinValue;
        lock (AboutGate) { _about = null; _aboutAt = DateTimeOffset.MinValue; }
        _lastError = null;
        lock (ConnectGate)
        {
            _connectCts?.Cancel();
            try { _listener?.Close(); } catch { /* already closed */ }
            _listener = null;
            _connectCts = null;
        }
    }

    // ---- helpers ----

    private static int FreeLoopbackPort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static string RandomUrlToken(int byteCount) => Base64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
