using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace KeepClip.Tests.Infrastructure;

public class PrivateApiSessionTests
{
    [Fact]
    public void Desktop_credentials_are_random_per_launch_and_cannot_authenticate_another_instance()
    {
        var first = PrivateApiSession.Create(false, "http://127.0.0.1:8770");
        var second = PrivateApiSession.Create(false, "http://127.0.0.1:8770");
        Assert.Matches("^[0-9A-F]{64}$", first.Credential);
        Assert.NotEqual(first.Credential, second.Credential);
        Assert.True(first.Authenticate("Bearer " + first.Credential));
        Assert.False(first.Authenticate("Bearer " + second.Credential));
        Assert.False(first.Authenticate("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("keepclip:" + first.Credential))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("password")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void Server_mode_cannot_start_without_an_explicit_strong_credential(string? credential)
        => Assert.Throws<InvalidOperationException>(() => PrivateApiSession.Create(true, "http://localhost:8770", credential));

    [Theory]
    [InlineData("http://0.0.0.0:8770", true)]
    [InlineData("https://[::]:8770", true)]
    [InlineData("http://192.0.2.10:8770", true)]
    [InlineData("https://192.0.2.10:8770", false)]
    [InlineData("http://localhost:8770;http://192.0.2.10:8771", false)]
    [InlineData("http://*:8770", true)]
    [InlineData("http://+:8770", true)]
    [InlineData("ftp://localhost:8770", false)]
    [InlineData("http://localhost:8770/path", false)]
    [InlineData("http://user:password@localhost:8770", false)]
    [InlineData("http://localhost:8770?token=secret", false)]
    [InlineData("", false)]
    public void Unsafe_listener_configurations_fail_closed(string urls, bool allowRemote)
        => Assert.Throws<InvalidOperationException>(() => PrivateApiSession.Create(true, urls, TestEnvironment.ApiToken, allowRemote));

    [Fact]
    public void Remote_access_requires_opt_in_tls_configured_host_and_owner_credentials()
    {
        var session = PrivateApiSession.Create(true, "https://192.0.2.10:8770", TestEnvironment.ApiToken, true);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.20");
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("192.0.2.10", 8770);
        Assert.True(session.AllowsRequest(http.Request));
        Assert.False(session.Authenticate(""));
        Assert.True(session.Authenticate("Bearer " + TestEnvironment.ApiToken));
        http.Request.Scheme = "http";
        Assert.False(session.AllowsRequest(http.Request));
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("evil.invalid", 8770);
        Assert.False(session.AllowsRequest(http.Request));
    }

    [Theory]
    [InlineData("/api/replay/config", "POST")]
    [InlineData("/api/replay/save", "POST")]
    [InlineData("/api/clips/1", "DELETE")]
    [InlineData("/api/clips/1/upload", "POST")]
    [InlineData("/api/clips/1/download", "POST")]
    [InlineData("/api/cloud/disconnect", "POST")]
    [InlineData("/api/cloud/connect", "POST")]
    [InlineData("/api/config", "POST")]
    [InlineData("/video/1", "GET")]
    public async Task Anonymous_requests_never_reach_any_privileged_handler(string path, string method)
    {
        int invoked = 0;
        var middleware = new PrivateApiMiddleware(_ => { invoked++; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost");
        context.Request.Scheme = "http";
        context.Request.Path = path;
        context.Request.Method = method;
        var session = PrivateApiSession.Create(false, "http://localhost:8770");
        await middleware.InvokeAsync(context, session);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal(0, invoked);
        context.Request.Headers.Authorization = "Bearer " + session.Credential;
        await middleware.InvokeAsync(context, session);
        Assert.Equal(1, invoked);
        Assert.True(context.User.Identity!.IsAuthenticated);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8770/api/clips", true)]
    [InlineData("http://127.0.0.1:8770/video/1", true)]
    [InlineData("http://127.0.0.1:8771/api/clips", false)]
    [InlineData("http://localhost:8770/api/clips", false)]
    [InlineData("https://127.0.0.1:8770/api/clips", false)]
    [InlineData("https://evil.invalid/", false)]
    [InlineData("http://127.0.0.1.evil.invalid:8770/", false)]
    [InlineData("file:///C:/private.html", false)]
    public void Desktop_header_is_only_injected_into_the_exact_application_origin(string uri, bool allowed)
    {
        var authentication = new DesktopRequestAuthentication("http://127.0.0.1:8770", TestEnvironment.ApiToken);
        Assert.Equal(allowed ? "Bearer " + TestEnvironment.ApiToken : null, authentication.AuthorizationFor(uri));
    }
}