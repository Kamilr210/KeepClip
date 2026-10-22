using System.Diagnostics;

namespace KeepClip.Services;

public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly UpdateChecker Checker = new(Http, Current, TimeProvider.System);

    private static string _phase = "idle";
    private static int _percent;
    private static string? _error;
    private static int _installing;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KeepClip-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

        public static Version Current =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public static Task<Dictionary<string, object?>> CheckAsync() => Checker.CheckAsync();

    public static object Status() => new { phase = _phase, percent = _percent, error = _error };

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
