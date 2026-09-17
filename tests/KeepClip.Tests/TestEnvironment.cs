using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace KeepClip.Tests;

internal static class TestEnvironment
{
    public static readonly string DataDir =
        Path.Combine(Path.GetTempPath(), "KeepClip.Tests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("KEEPCLIP_DATA_DIR", DataDir);
        Environment.SetEnvironmentVariable("KEEPCLIP_SERVER_ONLY", "1");
        Environment.SetEnvironmentVariable("KEEPCLIP_IDLE_EXIT", "0");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true);
        }
        catch
        {
        }
    }

    public static string NewTempDir(string name)
    {
        var dir = Path.Combine(DataDir, $"tmp-{name}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

internal static class TestDb
{
    private static int _clipCounter;

    public static void Reset()
    {
        Db.InitDb();
        using var con = Db.Open();
        con.Exec("DELETE FROM folder_clips; DELETE FROM segments; DELETE FROM folders; DELETE FROM clips;");
    }

    public static long AddClip(
        string game = "Gra", string? name = null, long size = 1000, double mtime = 1_700_000_000, string? dir = null)
    {
        name ??= $"clip{Interlocked.Increment(ref _clipCounter)}.mp4";
        var path = Path.Combine(dir ?? Path.Combine(TestEnvironment.DataDir, "clips", game), name);
        return new ClipRepository().Insert(game, name, path, size, mtime);
    }

    public static void Exec(string sql, params (string, object?)[] ps)
    {
        using var con = Db.Open();
        con.Exec(sql, ps);
    }

    public static long ScalarLong(string sql, params (string, object?)[] ps)
    {
        using var con = Db.Open();
        return con.ScalarLong(sql, ps);
    }
}

internal static class TestSettings
{
    public static string Path => System.IO.Path.Combine(Config.DataDir, "settings.json");

    public static void Reset()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}
