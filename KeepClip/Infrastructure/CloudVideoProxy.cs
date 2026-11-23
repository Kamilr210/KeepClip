using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KeepClip.Infrastructure;

internal sealed class CloudVideoProxy : IDisposable
{
    private static readonly Regex SafeRemoteIdRegex = new(@"^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled);
    private static readonly Regex SafeRangeRegex = new(@"^bytes=\d+-\d*$", RegexOptions.Compiled);

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly byte[] _tokenBytes;

    public CloudVideoProxy()
    {
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tokenBytes = Encoding.ASCII.GetBytes(_token);
        int port = FreeLoopbackPort();
        _baseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public Uri GetStreamUri(string remoteId)
    {
        if (!SafeRemoteIdRegex.IsMatch(remoteId))
            throw new ArgumentException("Invalid remote video id.", nameof(remoteId));
        return new($"{_baseUrl}video/{_token}/{Uri.EscapeDataString(remoteId)}");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleRequestAsync(ctx, ct), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            if (ctx.Request.HttpMethod is not ("GET" or "HEAD"))
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!path.StartsWith("/video/", StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var rest = path["/video/".Length..];
            var slash = rest.IndexOf('/');
            if (slash <= 0 || slash >= rest.Length - 1)
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            var providedToken = Encoding.ASCII.GetBytes(rest[..slash]);
            if (providedToken.Length != _tokenBytes.Length || !CryptographicOperations.FixedTimeEquals(providedToken, _tokenBytes))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            var remoteId = Uri.UnescapeDataString(rest[(slash + 1)..]);
            if (!SafeRemoteIdRegex.IsMatch(remoteId))
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                return;
            }

            var range = ctx.Request.Headers["Range"];
            if (!string.IsNullOrEmpty(range) && !SafeRangeRegex.IsMatch(range))
                range = null;

            using var resp = await CloudService.OpenRemoteVideoAsync(remoteId, string.IsNullOrEmpty(range) ? null : range, ct);
            ctx.Response.StatusCode = (int)resp.StatusCode;
            if (resp.Content.Headers.ContentType?.MediaType is { } mime)
                ctx.Response.ContentType = mime;
            if (resp.Content.Headers.ContentLength is { } len)
                ctx.Response.ContentLength64 = len;
            if (resp.Content.Headers.ContentRange is { } cr)
                ctx.Response.Headers["Content-Range"] = cr.ToString();
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            ctx.Response.Headers["Cache-Control"] = "no-store";

            if (ctx.Request.HttpMethod == "GET")
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await stream.CopyToAsync(ctx.Response.OutputStream, ct);
            }
        }
        catch { }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static int FreeLoopbackPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        _cts.Dispose();
    }
}
