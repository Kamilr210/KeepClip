using System.Net;
using System.Net.Sockets;

namespace KeepClip.Infrastructure;

internal sealed class CloudVideoProxy : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _baseUrl;

    public CloudVideoProxy()
    {
        int port = FreeLoopbackPort();
        _baseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public Uri GetStreamUri(string remoteId) => new($"{_baseUrl}video/{Uri.EscapeDataString(remoteId)}");

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

    private static async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!path.StartsWith("/video/", StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            var remoteId = Uri.UnescapeDataString(path["/video/".Length..]);
            var range = ctx.Request.Headers["Range"];
            using var resp = await CloudService.OpenRemoteVideoAsync(remoteId, string.IsNullOrEmpty(range) ? null : range, ct);
            ctx.Response.StatusCode = (int)resp.StatusCode;
            if (resp.Content.Headers.ContentType?.MediaType is { } mime)
                ctx.Response.ContentType = mime;
            if (resp.Content.Headers.ContentLength is { } len)
                ctx.Response.ContentLength64 = len;
            if (resp.Content.Headers.ContentRange is { } cr)
                ctx.Response.Headers["Content-Range"] = cr.ToString();
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            await stream.CopyToAsync(ctx.Response.OutputStream, ct);
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

