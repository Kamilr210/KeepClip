using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeepClip;

/// <summary>
/// Persistent user-tunable settings, stored as JSON next to the SQLite DB.
/// Kept separate from <see cref="Config"/> (compile-time constants) so the user
/// can change the clips folder without editing source. Mirrors <c>settings.py</c>.
/// </summary>
public static class Settings
{
    private static readonly string SettingsPath = Path.Combine(Config.DataDir, "settings.json");
    private static readonly object Gate = new();

    private static JsonObject Load()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();
        try
        {
            return JsonNode.Parse(File.ReadAllText(SettingsPath))?.AsObject() ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static void Save(JsonObject data)
    {
        Directory.CreateDirectory(Config.DataDir);
        var json = data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static string? StringOrNull(JsonObject o, string key)
    {
        if (o.TryGetPropertyValue(key, out var node) && node is JsonValue v &&
            v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
            return s;
        return null;
    }

    public static string GetClipsRoot()
        => StringOrNull(Load(), "clips_root") ?? Config.DefaultClipsRoot;

    public static void SetClipsRoot(string path)
    {
        lock (Gate)
        {
            var data = Load();
            data["clips_root"] = path;
            data["configured"] = true;
            Save(data);
        }
    }

    public static bool IsConfigured()
    {
        var o = Load();
        if (o.TryGetPropertyValue("configured", out var node) && node is JsonValue v &&
            v.TryGetValue<bool>(out var b))
            return b;
        return false;
    }

    public static string GetCutsRoot()
    {
        // Default: a subfolder inside the clips root so cuts live next to the game
        // folders and are scanned/accessible in-app (not stranded on the Desktop).
        return StringOrNull(Load(), "cuts_root")
               ?? Path.Combine(GetClipsRoot(), Config.CutsSubdir);
    }

    public static void SetCutsRoot(string path)
    {
        lock (Gate)
        {
            var data = Load();
            data["cuts_root"] = path;
            Save(data);
        }
    }

    /// <summary>Read an arbitrary persisted string setting (null if unset/empty).
    /// Used for small bits of remembered state like the Drive folder id.</summary>
    public static string? GetString(string key) => StringOrNull(Load(), key);

    /// <summary>Write an arbitrary persisted string setting.</summary>
    public static void SetString(string key, string value)
    {
        lock (Gate)
        {
            var data = Load();
            data[key] = value;
            Save(data);
        }
    }
}
