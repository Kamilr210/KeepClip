using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeepClip;

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

    public static string? GetString(string key) => StringOrNull(Load(), key);

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
