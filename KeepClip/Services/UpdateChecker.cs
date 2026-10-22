using System.Text.Json;

namespace KeepClip.Services;

internal sealed class UpdateChecker
{
    private const string ApiLatest = "https://api.github.com/repos/Kamilr210/KeepClip/releases/latest";
    private const string AssetName = "KeepClip-Setup.exe";

    private readonly object Gate = new();
    private Dictionary<string, object?>? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private readonly TimeSpan OkTtl = TimeSpan.FromHours(6);
    private readonly TimeSpan FailTtl = TimeSpan.FromMinutes(30);

    private readonly HttpClient Http;
    private readonly Version _current;
    private readonly TimeProvider _clock;

    internal UpdateChecker(HttpClient http, Version current, TimeProvider clock)
    {
        Http = http;
        _current = current;
        _clock = clock;
    }

    public async Task<Dictionary<string, object?>> CheckAsync()
    {
        lock (Gate)
        {
            if (_cached is not null)
            {
                var ttl = Equals(_cached.GetValueOrDefault("ok"), true) ? OkTtl : FailTtl;
                if (_clock.GetUtcNow() - _cachedAt < ttl) return _cached;
            }
        }
        var result = await FetchLatestAsync();
        lock (Gate) { _cached = result; _cachedAt = _clock.GetUtcNow(); }
        return result;
    }

    private async Task<Dictionary<string, object?>> FetchLatestAsync()
    {
        var cur = _current;
        var res = new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["current"] = $"{cur.Major}.{cur.Minor}.{cur.Build}",
            ["available"] = false,
        };
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Http.GetAsync(ApiLatest, cts.Token);
            if (!resp.IsSuccessStatusCode) return res;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return res;

            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    if (a.GetProperty("name").GetString() == AssetName)
                    { assetUrl = a.GetProperty("browser_download_url").GetString(); break; }

            res["ok"] = true;
            if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri) || assetUri.Scheme != Uri.UriSchemeHttps) assetUrl = null;
            res["latest"] = $"{latest.Major}.{latest.Minor}.{Math.Max(latest.Build, 0)}";
            res["available"] = latest > new Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0)) && assetUrl is not null;
            res["url"] = assetUrl;
            res["notes_url"] = root.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;
        }
        catch {  }
        return res;
    }
}