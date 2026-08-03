using System.Diagnostics;
using System.Text.Json;

namespace KeepClip.Services;

/// <summary>
/// Aktualizacje w miejscu: sprawdza najnowsze wydanie na GitHubie (wynik krótko
/// cache'owany), a na żądanie pobiera instalator do folderu tymczasowego i uruchamia go —
/// aplikacja wtedy kończy pracę, a instalator (ten sam AppId Inno) podmienia pliki bez
/// ruszania danych użytkownika. Plik pobrany przez HttpClient nie ma znacznika MotW,
/// więc SmartScreen nie blokuje tak uruchomionej aktualizacji.
/// </summary>
public static class UpdateService
{
    private const string ApiLatest = "https://api.github.com/repos/Kamilr210/KeepClip/releases/latest";
    private const string AssetName = "KeepClip-Setup.exe";

    private static readonly HttpClient Http = CreateClient();
    private static readonly object Gate = new();
    private static Dictionary<string, object?>? _cached;
    private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan OkTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailTtl = TimeSpan.FromMinutes(30);

    // Postęp instalacji dla UI: idle | downloading | starting | error.
    private static string _phase = "idle";
    private static int _percent;
    private static string? _error;
    private static int _installing;   // Interlocked: jedna instalacja naraz

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KeepClip-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Wersja uruchomionej aplikacji (z zasobu zestawu).</summary>
    public static Version Current =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    public static async Task<Dictionary<string, object?>> CheckAsync()
    {
        lock (Gate)
        {
            if (_cached is not null)
            {
                var ttl = Equals(_cached.GetValueOrDefault("ok"), true) ? OkTtl : FailTtl;
                if (DateTimeOffset.UtcNow - _cachedAt < ttl) return _cached;
            }
        }
        var result = await FetchLatestAsync();
        lock (Gate) { _cached = result; _cachedAt = DateTimeOffset.UtcNow; }
        return result;
    }

    private static async Task<Dictionary<string, object?>> FetchLatestAsync()
    {
        var cur = Current;
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
            if (!resp.IsSuccessStatusCode) return res;   // repo prywatne / brak release'u / limit API

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
            res["latest"] = $"{latest.Major}.{latest.Minor}.{Math.Max(latest.Build, 0)}";
            res["available"] = latest > new Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0)) && assetUrl is not null;
            res["url"] = assetUrl;
            res["notes_url"] = root.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;
        }
        catch { /* brak sieci itp. — cicho, baner po prostu się nie pokaże */ }
        return res;
    }

    public static object Status() => new { phase = _phase, percent = _percent, error = _error };

    /// <summary>Pobiera instalator i uruchamia go; zwraca false, gdy instalacja już trwa.</summary>
    public static bool BeginInstall()
    {
        if (Interlocked.CompareExchange(ref _installing, 1, 0) != 0) return false;
        _phase = "downloading"; _percent = 0; _error = null;
        _ = Task.Run(InstallCoreAsync);
        return true;
    }

    private static async Task InstallCoreAsync()
    {
        try
        {
            var check = await CheckAsync();
            var url = check.GetValueOrDefault("url") as string
                ?? throw new Exception(Strings.Get("update.noInstaller"));

            var setupPath = Path.Combine(Path.GetTempPath(), "KeepClip-Setup.exe");
            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(setupPath);
                var buf = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0) _percent = (int)(done * 100 / total);
                }
            }

            _phase = "starting"; _percent = 100;
            Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
            // Chwila na wystartowanie kreatora, potem zwalniamy pliki aplikacji.
            await Task.Delay(1500);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _phase = "error";
            _error = ex.Message;
            Interlocked.Exchange(ref _installing, 0);
        }
    }
}
