using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace KeepClip.Infrastructure;

internal sealed class OAuthSession
{
    private readonly OAuthBackend _backend;
    private readonly TimeProvider _clock;

    internal OAuthSession(OAuthBackend backend, TimeProvider? clock = null)
    {
        _backend = backend;
        _clock = clock ?? TimeProvider.System;
    }

    private readonly SemaphoreSlim TokenGate = new(1, 1);
    private readonly object TokenStateGate = new();
    private long _connectionVersion;
    private string? _accessToken;
    private DateTimeOffset _accessExpiry = DateTimeOffset.MinValue;

    private readonly object AboutGate = new();
    private GoogleDrive.AboutInfo? _about;
    private DateTimeOffset _aboutAt = DateTimeOffset.MinValue;
    private readonly TimeSpan AboutTtl = TimeSpan.FromSeconds(20);
    private string? _lastError;

    private readonly object ConnectGate = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _connectCts;

    public bool IsConfigured() => _backend.IsConfigured;

    public bool IsConnected() =>
        !string.IsNullOrEmpty(_backend.ReadToken());

    public async Task<Dictionary<string, object?>> StatusAsync()
    {
        var st = new Dictionary<string, object?>
        {
            ["configured"] = IsConfigured(),
            ["connected"] = false,
        };
        if (!IsConfigured() || !IsConnected()) return st;
        long version;
        lock (TokenStateGate) version = _connectionVersion;

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
            lock (TokenStateGate)
                if (version == _connectionVersion) DisconnectLocal();
            st["connected"] = IsConnected();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            st["connected"] = IsConnected();
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

    internal async Task<string> GetAccessTokenAsync()
    {
        await TokenGate.WaitAsync(CancellationToken.None);
        try
        {
            string refresh;
            long version;
            lock (TokenStateGate)
            {
                if (_accessToken != null && _clock.GetUtcNow() < _accessExpiry)
                    return _accessToken;
                version = _connectionVersion;
                refresh = _backend.ReadToken()
                    ?? throw new InvalidOperationException(Strings.Get("cloud.notConnected"));
            }
            var (id, secret) = _backend.LoadClient();
            try
            {
                var tok = await _backend.RefreshAsync(id, secret, refresh);
                lock (TokenStateGate)
                {
                    if (version != _connectionVersion) throw new OperationCanceledException("OAuth session changed while refreshing the token.");
                    _accessToken = tok.AccessToken;
                    _accessExpiry = _clock.GetUtcNow().AddSeconds(Math.Max(0, tok.ExpiresInSeconds - 60));
                    if (!string.IsNullOrEmpty(tok.RefreshToken))
                        _backend.WriteToken(tok.RefreshToken!);
                    return _accessToken;
                }
            }
            catch (GoogleDrive.GoogleApiException ex) when (ex.IsInvalidGrant)
            {
                lock (TokenStateGate)
                    if (version == _connectionVersion) DisconnectLocal();
                throw;
            }
        }
        finally { TokenGate.Release(); }
    }

    private async Task<GoogleDrive.AboutInfo> GetAboutAsync(string token)
    {
        long version;
        lock (TokenStateGate) version = _connectionVersion;
        lock (AboutGate)
        {
            if (_about is { } cached && _clock.GetUtcNow() - _aboutAt < AboutTtl)
                return cached;
        }
        var about = await _backend.GetAboutAsync(token);
        lock (TokenStateGate)
        {
            if (version != _connectionVersion) throw new OperationCanceledException("OAuth session changed while loading the profile.");
            lock (AboutGate) { _about = about; _aboutAt = _clock.GetUtcNow(); _lastError = null; }
        }
        return about;
    }

    public async Task<GoogleDrive.ProfilePhoto?> GetProfilePhotoAsync()
    {
        if (!IsConfigured() || !IsConnected()) return null;

        var token = await GetAccessTokenAsync();
        var about = await GetAboutAsync(token);
        if (string.IsNullOrWhiteSpace(about.Photo)) return null;

        return await _backend.DownloadPhotoAsync(about.Photo);
    }

    internal void InvalidateAbout() { lock (AboutGate) { _aboutAt = DateTimeOffset.MinValue; } }

    public string BeginConnect()
    {
        var (clientId, _) = _backend.LoadClient();

        int port = FreeLoopbackPort();
        string redirectUri = $"http://127.0.0.1:{port}/";
        string verifier = RandomUrlToken(64);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = RandomUrlToken(24);

        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        lock (ConnectGate)
        {
            _connectCts?.Cancel();
            try { _listener?.Close(); } catch { }
            _listener = listener;
            _connectCts = cts;
        }

        // Sprzątanie listenera musi wykonać się także po anulowaniu logowania przed startem zadania.
        _ = Task.Run(async () =>
        {
            try { await CaptureRedirectAsync(listener, clientId, verifier, state, redirectUri, cts.Token); }
            finally { cts.Dispose(); }
        }, CancellationToken.None);

        return $"{GoogleDrive.AuthEndpoint}?" + string.Join("&", new[]
        {
            "client_id="            + Uri.EscapeDataString(clientId),
            "redirect_uri="         + Uri.EscapeDataString(redirectUri),
            "response_type=code",
            "scope="                + Uri.EscapeDataString(GoogleDrive.Scope),
            "code_challenge="       + challenge,
            "code_challenge_method=S256",
            "state="                + state,
            "access_type=offline",
            "prompt=consent",
        });
    }

    private async Task CaptureRedirectAsync(
        HttpListener listener, string clientId, string verifier, string expectedState,
        string redirectUri, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var ctxTask = listener.GetContextAsync();
                var done = await Task.WhenAny(ctxTask, Task.Delay(Timeout.Infinite, ct));
            if (done != ctxTask) break;
                var ctx = await ctxTask;

                var q = ctx.Request.QueryString;
                string? code = q["code"];
                string? state = q["state"];
                string? error = q["error"];

                if (code is null && error is null) { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }
                if (state != expectedState)
                {
                    ctx.Response.StatusCode = 400;
                    WriteHtml(ctx, "Nieprawidłowe logowanie", "Stan autoryzacji nie pasuje do rozpoczętego logowania.");
                    continue;
                }

                if (error is null && code is not null)
                {
                    try
                    {
                        var (_, secret) = _backend.LoadClient();
                        var tok = await _backend.ExchangeCodeAsync(clientId, secret, code, verifier, redirectUri);
                        if (string.IsNullOrEmpty(tok.RefreshToken))
                            throw new InvalidOperationException(Strings.Get("drive.noRefreshToken"));

                        string? email = null;
                        GoogleDrive.AboutInfo? profile = null;
                        try
                        {
                            var about = await _backend.GetAboutAsync(tok.AccessToken);
                            email = about.Email;
                            profile = about;
                        }
                        catch { }

                        await TokenGate.WaitAsync(ct);
                        try
                        {
                            lock (TokenStateGate)
                            {
                                ct.ThrowIfCancellationRequested();
                                _connectionVersion++;
                                _backend.WriteToken(tok.RefreshToken!, email);
                                _accessToken = tok.AccessToken;
                                _accessExpiry = _clock.GetUtcNow().AddSeconds(Math.Max(0, tok.ExpiresInSeconds - 60));
                                lock (AboutGate)
                                {
                                    _about = profile;
                                    _aboutAt = profile.HasValue ? _clock.GetUtcNow() : DateTimeOffset.MinValue;
                                }
                            }
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
            break;
            }
        }
        catch (Exception ex) { _lastError = ex.Message; }
        finally
        {
            try { listener.Close(); } catch { }
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
        catch { }
    }

    public async Task DisconnectAsync()
    {
        var refresh = _backend.ReadToken();
        DisconnectLocal();
        if (!string.IsNullOrEmpty(refresh))
            await _backend.RevokeAsync(refresh!);
    }

    private void DisconnectLocal()
    {
        lock (TokenStateGate)
        {
            _connectionVersion++;
            _backend.DeleteToken();
            _accessToken = null;
            _accessExpiry = DateTimeOffset.MinValue;
        }
        lock (AboutGate) { _about = null; _aboutAt = DateTimeOffset.MinValue; }
        _lastError = null;
        lock (ConnectGate)
        {
            _connectCts?.Cancel();
            try { _listener?.Close(); } catch { }
            _listener = null;
            _connectCts = null;
        }
    }

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