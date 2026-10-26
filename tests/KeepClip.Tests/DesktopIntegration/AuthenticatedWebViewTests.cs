using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using FormsApplication = System.Windows.Forms.Application;

namespace KeepClip.Tests.DesktopIntegration;

[Trait("Category", "Desktop")]
public class AuthenticatedWebViewTests
{
    [Fact]
    public async Task Native_transport_authenticates_ui_media_and_fetch_without_leaking_to_another_origin()
    {
        var root = TestEnvironment.NewTempDir("private-webview");
        var video = Path.Combine(root, "video.mp4");
        var encoded = new MediaProcessRunner().Run(Config.Ffmpeg, new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", video }, 30);
        Assert.False(encoded.Faulted);
        Assert.True(encoded.ExitCode == 0, encoded.StdErr);

        var foreignBuilder = WebApplication.CreateBuilder();
        foreignBuilder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var foreign = foreignBuilder.Build();
        var foreignHeaders = new ConcurrentBag<string>();
        foreign.MapGet("/probe", (HttpContext context) =>
        {
            foreignHeaders.Add(context.Request.Headers.Authorization.ToString());
            context.Response.Headers.AccessControlAllowOrigin = "*";
            return Results.Text("foreign test server");
        });
        await foreign.StartAsync();
        var foreignUrl = foreign.Urls.Single();

        var session = PrivateApiSession.Create(false, "http://127.0.0.1:8770");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(session);
        await using var app = builder.Build();
        app.UseMiddleware<PrivateApiMiddleware>();
        var received = new ConcurrentBag<string>();
        app.Use(async (context, next) => { received.Add(context.Request.Path); await next(); });
        app.MapGet("/", () => Results.Content("<html><body><img id='image' src='/image'><video id='video' preload='metadata' src='/video'></video><script>Promise.all([fetch('/api/probe'),fetch('/video',{headers:{Range:'bytes=0-31'}}),fetch('" + foreignUrl + "/probe')]).then(r=>window.probe=r.map(x=>x.status)).catch(()=>window.probe=['failed']);</script></body></html>", "text/html"));
        app.MapGet("/api/probe", () => Results.Json(new { ok = true }));
        app.MapGet("/image", () => Results.Bytes(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8p8AAAAASUVORK5CYII="), "image/png"));
        app.MapGet("/video", () => Results.File(video, "video/mp4", enableRangeProcessing: true));
        await app.StartAsync();
        var url = app.Urls.Single();
        using var anonymous = new HttpClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url + "/video")).StatusCode);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form { ShowInTaskbar = false, Opacity = 0, Width = 400, Height = 300 };
            using var view = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);
            form.Shown += async (_, _) =>
            {
                try
                {
                    var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "profile"));
                    await view.EnsureCoreWebView2Async(environment);
                    var core = view.CoreWebView2;
                    new DesktopRequestAuthentication(url, session.Credential).Attach(core);
                    var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    core.NavigationCompleted += (_, e) =>
                    {
                        if (e.IsSuccess) navigation.TrySetResult();
                        else navigation.TrySetException(new InvalidOperationException("Native navigation failed: " + e.WebErrorStatus));
                    };
                    core.Navigate(url);
                    await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    while (true)
                    {
                        var raw = await core.ExecuteScriptAsync("JSON.stringify({probe:window.probe,video:document.getElementById('video').readyState>=1,image:document.getElementById('image').naturalWidth>0})");
                        using var status = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
                        if (status.RootElement.TryGetProperty("probe", out var probe) && status.RootElement.GetProperty("video").GetBoolean() && status.RootElement.GetProperty("image").GetBoolean())
                        {
                            Assert.Equal(new[] { 200, 206, 200 }, probe.EnumerateArray().Select(value => value.GetInt32()));
                            break;
                        }
                        await Task.Delay(50, timeout.Token);
                    }
                    var html = await core.ExecuteScriptAsync("document.documentElement.outerHTML");
                    Assert.DoesNotContain(session.Credential, html);
                    completion.TrySetResult();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { form.Close(); }
            };
            FormsApplication.Run(form);
        }) { IsBackground = true, Name = "KeepClip-PrivateSession-Test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(75));
        Assert.True(thread.Join(5000));
        Assert.Contains("/", received);
        Assert.Contains("/image", received);
        Assert.Contains("/video", received);
        Assert.Contains("/api/probe", received);
        Assert.NotEmpty(foreignHeaders);
        Assert.All(foreignHeaders, value => Assert.Equal("", value));
        await app.StopAsync();
        await foreign.StopAsync();
    }
}