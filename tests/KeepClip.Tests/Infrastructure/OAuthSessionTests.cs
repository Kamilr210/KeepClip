using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace KeepClip.Tests.Infrastructure;

public class OAuthSessionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Backend : OAuthBackend
    {
        public bool Configured = true;
        public string? StoredToken = "refresh-test";
        public int RefreshCalls;
        public int AboutCalls;
        public int ExchangeCalls;
        public string? Verifier;
        public List<string> Revoked = new();
        public Func<Task<GoogleDrive.TokenResult>> Refresh = () => Task.FromResult(new GoogleDrive.TokenResult("access-test", null, 3600));
        public Func<Task<GoogleDrive.AboutInfo>> About = () => Task.FromResult(new GoogleDrive.AboutInfo("Tester", "test@example.invalid", "https://lh3.googleusercontent.com/test", 12, 100));
        public Func<Task<GoogleDrive.TokenResult>> Exchange = () => Task.FromResult(new GoogleDrive.TokenResult("access-login", "refresh-login", 3600));
        public override bool IsConfigured => Configured;
        public override string? ReadToken() => StoredToken;
        public override void WriteToken(string token, string? email = null) => StoredToken = token;
        public override void DeleteToken() => StoredToken = null;
        public override string LoadClientId() => "test-client";
        public override Task<GoogleDrive.TokenResult> RefreshAsync(string id, string refresh)
        {
            Assert.Equal("test-client", id);
            Assert.Equal(StoredToken, refresh);
            RefreshCalls++;
            return Refresh();
        }
        public override Task<GoogleDrive.AboutInfo> GetAboutAsync(string token) { AboutCalls++; return About(); }
        public override Task<GoogleDrive.TokenResult> ExchangeCodeAsync(string id, string code, string verifier, string redirect)
        {
            Assert.Equal("test-client", id);
            Assert.Equal("test-code", code);
            Verifier = verifier;
            ExchangeCalls++;
            return Exchange();
        }
        public override Task<GoogleDrive.ProfilePhoto> DownloadPhotoAsync(string url)
        {
            Assert.Equal("https://lh3.googleusercontent.com/test", url);
            return Task.FromResult(new GoogleDrive.ProfilePhoto(new byte[] { 1, 2 }, "image/png"));
        }
        public override Task RevokeAsync(string token) { Revoked.Add(token); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Concurrent_token_requests_share_one_refresh_and_cache_the_result()
    {
        var completion = new TaskCompletionSource<GoogleDrive.TokenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { Refresh = () => completion.Task };
        var session = new OAuthSession(backend, new Clock());
        var pending = Enumerable.Range(0, 10).Select(_ => session.GetAccessTokenAsync()).ToArray();
        Assert.All(pending, task => Assert.False(task.IsCompleted));
        Assert.Equal(1, backend.RefreshCalls);
        completion.SetResult(new GoogleDrive.TokenResult("access-test", null, 3600));
        var tokens = await Task.WhenAll(pending);
        Assert.All(tokens, token => Assert.Equal("access-test", token));
        Assert.Equal(1, backend.RefreshCalls);
    }

    [Fact]
    public async Task Expiry_refreshes_the_token_and_persists_a_rotated_refresh_token()
    {
        var clock = new Clock();
        var backend = new Backend { Refresh = () => Task.FromResult(new GoogleDrive.TokenResult("access", "rotated", 120)) };
        var session = new OAuthSession(backend, clock);
        await session.GetAccessTokenAsync();
        Assert.Equal("rotated", backend.StoredToken);
        clock.Now += TimeSpan.FromSeconds(59);
        await session.GetAccessTokenAsync();
        Assert.Equal(1, backend.RefreshCalls);
        clock.Now += TimeSpan.FromSeconds(1);
        await session.GetAccessTokenAsync();
        Assert.Equal(2, backend.RefreshCalls);
    }

    [Fact]
    public async Task Short_lived_tokens_are_never_cached_beyond_their_actual_expiry()
    {
        var clock = new Clock();
        var backend = new Backend { Refresh = () => Task.FromResult(new GoogleDrive.TokenResult("short", null, 30)) };
        var session = new OAuthSession(backend, clock);
        await session.GetAccessTokenAsync();
        clock.Now += TimeSpan.FromSeconds(31);
        await session.GetAccessTokenAsync();
        Assert.Equal(2, backend.RefreshCalls);
    }

    [Fact]
    public async Task Missing_or_revoked_credentials_do_not_produce_an_access_token()
    {
        var backend = new Backend { StoredToken = null };
        var session = new OAuthSession(backend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetAccessTokenAsync());
        Assert.Equal(0, backend.RefreshCalls);
        backend.StoredToken = "expired";
        backend.Refresh = () => Task.FromException<GoogleDrive.TokenResult>(new GoogleDrive.GoogleApiException("invalid_grant"));
        Assert.Equal(false, (await session.StatusAsync())["connected"]);
        Assert.Null(backend.StoredToken);
    }

    [Fact]
    public async Task Profile_cache_expires_and_can_be_invalidated_after_storage_changes()
    {
        var clock = new Clock();
        var backend = new Backend();
        var session = new OAuthSession(backend, clock);
        var first = await session.StatusAsync();
        Assert.Equal(true, first["connected"]);
        Assert.Equal("Tester", first["name"]);
        Assert.Equal(12L, first["usage"]);
        await session.StatusAsync();
        Assert.Equal(1, backend.AboutCalls);
        clock.Now += TimeSpan.FromSeconds(20);
        await session.StatusAsync();
        Assert.Equal(2, backend.AboutCalls);
        session.InvalidateAbout();
        await session.StatusAsync();
        Assert.Equal(3, backend.AboutCalls);
    }

    [Fact]
    public async Task Temporary_profile_error_preserves_the_last_known_profile_and_connection()
    {
        var backend = new Backend();
        var session = new OAuthSession(backend);
        await session.StatusAsync();
        session.InvalidateAbout();
        backend.About = () => Task.FromException<GoogleDrive.AboutInfo>(new IOException("offline"));
        var status = await session.StatusAsync();
        Assert.Equal(true, status["connected"]);
        Assert.Equal("Tester", status["name"]);
        Assert.Equal("offline", status["error"]);
    }

    [Fact]
    public async Task Unconfigured_session_does_not_attempt_network_operations()
    {
        var backend = new Backend { Configured = false };
        var session = new OAuthSession(backend);
        Assert.Equal(false, (await session.StatusAsync())["configured"]);
        Assert.Null(await session.GetProfilePhotoAsync());
        Assert.Equal(0, backend.RefreshCalls);
    }

    [Fact]
    public async Task Profile_photo_uses_the_cached_profile_and_returns_bytes()
    {
        var backend = new Backend();
        var session = new OAuthSession(backend);
        var photo = await session.GetProfilePhotoAsync();
        Assert.NotNull(photo);
        Assert.Equal(new byte[] { 1, 2 }, photo.Value.Data);
        backend.About = () => Task.FromResult(new GoogleDrive.AboutInfo(null, null, null, null, null));
        session.InvalidateAbout();
        Assert.Null(await session.GetProfilePhotoAsync());
    }

    [Fact]
    public async Task Disconnect_clears_cached_access_and_revokes_the_saved_refresh_token()
    {
        var backend = new Backend();
        var session = new OAuthSession(backend);
        await session.GetAccessTokenAsync();
        await session.DisconnectAsync();
        Assert.False(session.IsConnected());
        Assert.Equal(new[] { "refresh-test" }, backend.Revoked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Inflight_refresh_cannot_restore_credentials_after_disconnect()
    {
        var completion = new TaskCompletionSource<GoogleDrive.TokenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { Refresh = () => completion.Task };
        var session = new OAuthSession(backend);
        var pending = session.GetAccessTokenAsync();
        Assert.Equal(1, backend.RefreshCalls);
        await session.DisconnectAsync();
        completion.SetResult(new GoogleDrive.TokenResult("late-access", "late-refresh", 3600));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(session.IsConnected());
        Assert.Null(backend.StoredToken);
    }

    [Fact]
    public async Task Inflight_profile_request_cannot_report_a_disconnected_session_as_connected()
    {
        var completion = new TaskCompletionSource<GoogleDrive.AboutInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { About = () => completion.Task };
        var session = new OAuthSession(backend);
        var pending = session.StatusAsync();
        Assert.Equal(1, backend.AboutCalls);
        await session.DisconnectAsync();
        completion.SetResult(new GoogleDrive.AboutInfo("stale user", null, null, null, null));
        var status = await pending;
        Assert.Equal(false, status["connected"]);
        Assert.False(status.ContainsKey("name"));
    }

    [Fact]
    public async Task Authorization_uses_pkce_and_saves_credentials_after_a_valid_local_callback()
    {
        var backend = new Backend { StoredToken = null };
        var session = new OAuthSession(backend);
        var authorize = new Uri(session.BeginConnect());
        try
        {
            var query = QueryHelpers.ParseQuery(authorize.Query);
            Assert.Equal("accounts.google.com", authorize.Host);
            Assert.Equal("S256", query["code_challenge_method"].ToString());
            Assert.Equal(GoogleDrive.Scope, query["scope"].ToString());
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var callback = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?> { ["code"] = "test-code", ["state"] = query["state"].ToString() });
            var response = await http.GetAsync(callback);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Połączono", await response.Content.ReadAsStringAsync());
            Assert.Equal("refresh-login", backend.StoredToken);
            Assert.Equal(1, backend.ExchangeCalls);
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(backend.Verifier!))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(query["code_challenge"].ToString(), challenge);
        }
        finally { await session.DisconnectAsync(); }
    }

    [Fact]
    public async Task Invalid_oauth_state_is_rejected_without_consuming_the_login_attempt()
    {
        var backend = new Backend { StoredToken = null };
        var session = new OAuthSession(backend);
        var query = QueryHelpers.ParseQuery(new Uri(session.BeginConnect()).Query);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var callback = query["redirect_uri"].ToString();
            var rejected = await http.GetAsync(QueryHelpers.AddQueryString(callback, new Dictionary<string, string?> { ["code"] = "test-code", ["state"] = "incorrect" }));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(0, backend.ExchangeCalls);
            var accepted = await http.GetAsync(QueryHelpers.AddQueryString(callback, new Dictionary<string, string?> { ["code"] = "test-code", ["state"] = query["state"].ToString() }));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal("refresh-login", backend.StoredToken);
        }
        finally { await session.DisconnectAsync(); }
    }

    [Fact]
    public async Task OAuth_error_text_is_html_encoded_and_does_not_exchange_a_code()
    {
        var backend = new Backend { StoredToken = null };
        var session = new OAuthSession(backend);
        var query = QueryHelpers.ParseQuery(new Uri(session.BeginConnect()).Query);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var callback = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?> { ["error"] = "<script>alert('test')</script>", ["state"] = query["state"].ToString() });
            var html = await http.GetStringAsync(callback);
            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Equal(0, backend.ExchangeCalls);
            Assert.Null(backend.StoredToken);
        }
        finally { await session.DisconnectAsync(); }
    }

    [Fact]
    public async Task New_login_does_not_reuse_the_previous_accounts_profile_when_the_new_profile_is_unavailable()
    {
        var backend = new Backend();
        var session = new OAuthSession(backend);
        Assert.Equal("Tester", (await session.StatusAsync())["name"]);
        backend.About = () => Task.FromException<GoogleDrive.AboutInfo>(new IOException("new profile unavailable"));
        var query = QueryHelpers.ParseQuery(new Uri(session.BeginConnect()).Query);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var callback = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?> { ["code"] = "test-code", ["state"] = query["state"].ToString() });
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(callback)).StatusCode);
            var status = await session.StatusAsync();
            Assert.True((bool)status["connected"]!);
            Assert.False(status.ContainsKey("name"));
            Assert.Equal("new profile unavailable", status["error"]);
        }
        finally { await session.DisconnectAsync(); }
    }
}