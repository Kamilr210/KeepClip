namespace KeepClip;

public static class CrashLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(Config.DataDir, "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("nieobsłużony wyjątek", e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "(brak szczegółów)"));

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("nieobserwowane zadanie", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception? ex)
    {
        Console.Error.WriteLine($"{source}: {ex}");
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Config.DataDir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Delete(FilePath);

                var version = typeof(CrashLog).Assembly.GetName().Version;

                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] KeepClip {version} - {source}{Environment.NewLine}" +
                    $"{ex}{Environment.NewLine}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }
        catch
        {
        }
    }
}
