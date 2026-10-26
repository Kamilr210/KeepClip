using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;

namespace KeepClip.Tests.Infrastructure;

public class PrivateApiProcessTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8770", false, false)]
    [InlineData("http://0.0.0.0:8770", true, false)]
    [InlineData("http://127.0.0.1:8770", true, true)]
    public async Task Unsafe_startup_fails_before_database_or_background_services_are_initialized(string urls, bool token, bool alternateEndpoint)
    {
        var root = TestEnvironment.NewTempDir("rejected-server");
        var info = Server(root, urls, token);
        if (alternateEndpoint) info.ArgumentList.Add("--Kestrel:Endpoints:Unsafe:Url=http://0.0.0.0:8771");
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotEqual(0, process.ExitCode);
            Assert.False(File.Exists(Path.Combine(root, "klipy.db")));
            Assert.DoesNotContain(TestEnvironment.ApiToken, await stdout + await stderr);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task Real_application_requires_authentication_on_its_loopback_socket()
    {
        var root = TestEnvironment.NewTempDir("authenticated-server");
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var url = $"http://127.0.0.1:{port}";
        using var process = Process.Start(Server(root, url, true))!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(url) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                if (process.HasExited) throw new InvalidOperationException("Test server exited before accepting requests: " + await stderr);
                try
                {
                    using var response = await http.GetAsync("/api/ping", timeout.Token);
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                    break;
                }
                catch (HttpRequestException) { await Task.Delay(50, timeout.Token); }
            }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestEnvironment.ApiToken);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/ping", timeout.Token)).StatusCode);
            var body = await http.GetStringAsync("/api/config", timeout.Token);
            Assert.DoesNotContain(TestEnvironment.ApiToken, body);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('0', 64));
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/config", timeout.Token)).StatusCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        Assert.DoesNotContain(TestEnvironment.ApiToken, await stdout + await stderr);
    }

    private static ProcessStartInfo Server(string dataDirectory, string urls, bool token)
    {
        File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), JsonSerializer.Serialize(new { clips_root = dataDirectory, replay_enabled = "0" }));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { typeof(Program).Assembly.Location, "server-only", "--urls", urls }) info.ArgumentList.Add(arg);
        info.Environment["KEEPCLIP_DATA_DIR"] = dataDirectory;
        info.Environment["KEEPCLIP_SERVER_ONLY"] = "1";
        info.Environment["KEEPCLIP_IDLE_EXIT"] = "0";
        info.Environment["KEEPCLIP_ALLOW_REMOTE"] = "0";
        if (token) info.Environment["KEEPCLIP_API_TOKEN"] = TestEnvironment.ApiToken;
        else info.Environment.Remove("KEEPCLIP_API_TOKEN");
        return info;
    }
}