using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeepClip.Infrastructure;

// Wspólny klient OAuth jest dostarczany z instalatorem, a token odświeżania konkretnego
// użytkownika trafia wyłącznie do Menedżera poświadczeń Windows.
public static class OAuthService
{
    // SemaphoreSlim chroni asynchroniczne odświeżanie tokenu przed równoległymi żądaniami.
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _accessExpiry = DateTimeOffset.MinValue;

    // Krótka pamięć podręczna chroni Dysk Google przed częstymi zapytaniami interfejsu.
    private static readonly object AboutGate = new();
    private static GoogleDrive.AboutInfo? _about;
    private static DateTimeOffset _aboutAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan AboutTtl = TimeSpan.FromSeconds(20);
    private static string? _lastError;

    // Jednocześnie może trwać tylko jeden przepływ PKCE z lokalnym adresem zwrotnym.
    private static readonly object ConnectGate = new();
    private static HttpListener? _listener;
    private static CancellationTokenSource? _connectCts;

    public static bool IsConfigured() => File.Exists(Config.GoogleClientPath);

    public static bool IsConnected() =>
        !string.IsNullOrEmpty(CredentialStore.Read(Config.DriveTokenTarget));

    private static (string clientId, string clientSecret) LoadClient()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Config.GoogleClientPath));
        var root = doc.RootElement;
    // Google opakowuje dane klienta w obiekt `installed` albo `web`.
        var creds = root.TryGetProperty("installed", out var ins) ? ins
                  : root.TryGetProperty("web", out var web) ? web
                  : root;
        string id = (creds.TryGetProperty("client_id", out var cid) ? cid.GetString() : null)
            ?? throw new InvalidOperationException("Brak client_id w google_client.json.");
        string secret = creds.TryGetProperty("client_secret", out var cs) ? cs.GetString() ?? "" : "";
        return (id, secret);
    }

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
            // Cofnięta lub wygasła zgoda wymaga ponownego połączenia konta.
            DisconnectLocal();
            st["connected"] = false;
        }
        catch (Exception ex)
        {
            // Błąd przejściowy nie rozłącza konta; zachowujemy ostatnie dane, aby panel
            // nie migał pustym stanem.
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
                // Minuta zapasu zapobiega użyciu tokenu wygasającego w trakcie żądania.
                _accessExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tok.ExpiresInSeconds - 60));
        if (!string.IsNullOrEmpty(tok.RefreshToken)) // Aktualizuje token, jeżeli Google go zmieniło.
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

    internal static void InvalidateAbout() { lock (AboutGate) { _aboutAt = DateTimeOffset.MinValue; } }

    public static string BeginConnect()
    {
        var (clientId, _) = LoadClient(); // Sprawdza konfigurację przed rozpoczęciem operacji.

        int port = FreeLoopbackPort();
        string redirectUri = $"http://127.0.0.1:{port}/";
        string verifier = RandomUrlToken(64);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = RandomUrlToken(24);

        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // Zgodne z limitem odpytywania interfejsu.
        lock (ConnectGate)
        {
            _connectCts?.Cancel();
            try { _listener?.Close(); } catch { }
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
            "access_type=offline",  // Prosi o token odświeżania.
            "prompt=consent",       // Wymusza jego wydanie także przy ponownej zgodzie.
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
            if (done != ctxTask) break; // Operację anulowano albo przekroczono czas oczekiwania.
                var ctx = await ctxTask;

                var q = ctx.Request.QueryString;
                string? code = q["code"];
                string? state = q["state"];
                string? error = q["error"];

    // Żądanie ikony strony nie może zakończyć oczekiwania na odpowiedź OAuth.
                if (code is null && error is null) { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }

                if (error is null && code is not null && state == expectedState)
                {
                    try
                    {
                        var (_, secret) = LoadClient();
                        var tok = await GoogleDrive.ExchangeCodeAsync(clientId, secret, code, verifier, redirectUri);
                        if (string.IsNullOrEmpty(tok.RefreshToken))
                            throw new InvalidOperationException("Google nie zwrócił tokenu odświeżania.");

                        // E-mail opisuje wpis w Menedżerze poświadczeń, ale nie jest wymagany.
                        string? email = null;
                        try
                        {
                            var about = await GoogleDrive.GetAboutAsync(tok.AccessToken);
                            email = about.Email;
                            lock (AboutGate) { _about = about; _aboutAt = DateTimeOffset.UtcNow; }
                        }
                        catch { }

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
            break; // Pierwsze właściwe przekierowanie kończy próbę, niezależnie od wyniku.
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
