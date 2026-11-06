using System.Text;
using System.Text.Json;

namespace KeepClip.Infrastructure;

internal sealed class InstalledGameCatalog(string epicManifestDirectory, TimeProvider? clock = null)
{
    private const int MaximumManifestBytes = 256 * 1024;
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Snapshot> _steam = new(StringComparer.OrdinalIgnoreCase);
    private Snapshot? _epic;

    private sealed record Installation(string Directory, string Name);
    private sealed record Snapshot(long Timestamp, List<Installation> Games);

    internal string? Find(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath)) return null;
        try
        {
            var path = Path.GetFullPath(executablePath);
            lock (_gate)
            {
                var steamDirectory = SteamDirectory(path);
                if (steamDirectory is not null)
                {
                    if (!_steam.TryGetValue(steamDirectory, out var snapshot) || Expired(snapshot))
                    {
                        if (_steam.Count >= 16) _steam.Clear();
                        _steam[steamDirectory] = snapshot = new(_clock.GetTimestamp(), ReadSteam(steamDirectory));
                    }
                    if (Match(path, snapshot.Games) is { } name) return name;
                }
                if (_epic is null || Expired(_epic)) _epic = new(_clock.GetTimestamp(), ReadEpic(epicManifestDirectory));
                return Match(path, _epic.Games);
            }
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private bool Expired(Snapshot snapshot) => _clock.GetElapsedTime(snapshot.Timestamp) >= TimeSpan.FromMinutes(1);

    private static string? SteamDirectory(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        for (int depth = 0; directory is not null && depth < 32; depth++, directory = directory.Parent)
            if (directory.Name.Equals("common", StringComparison.OrdinalIgnoreCase) && directory.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true)
                return directory.Parent.FullName;
        return null;
    }

    private static string? Match(string path, List<Installation> games)
    {
        string? name = null;
        int longest = 0;
        foreach (var game in games)
        {
            if (game.Directory.Length < longest || !path.StartsWith(game.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (game.Directory.Length == longest && !string.Equals(name, game.Name, StringComparison.OrdinalIgnoreCase)) name = null;
            else if (game.Directory.Length > longest) { name = game.Name; longest = game.Directory.Length; }
        }
        return name;
    }

    private static List<Installation> ReadSteam(string directory)
    {
        var games = new List<Installation>();
        foreach (var file in ManifestFiles(directory, "appmanifest_*.acf"))
        {
            var text = ReadManifest(file);
            if (text is null || SteamAppManifest.Parse(text) is not { } manifest) continue;
            var folder = manifest.InstallDirectory;
            if (folder is "." or ".." || folder != Path.GetFileName(folder) || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            Add(games, Path.Combine(directory, "common", folder), manifest.Name);
        }
        return games;
    }

    private static List<Installation> ReadEpic(string directory)
    {
        var games = new List<Installation>();
        foreach (var file in ManifestFiles(directory, "*.item"))
        {
            var text = ReadManifest(file);
            if (text is null) continue;
            try
            {
                using var json = JsonDocument.Parse(text);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind != JsonValueKind.False) continue;
                var app = JsonString(root, "AppName");
                var parent = JsonString(root, "MainGameAppName");
                if (!string.IsNullOrEmpty(parent) && !string.Equals(app, parent, StringComparison.Ordinal)) continue;
                Add(games, JsonString(root, "InstallLocation"), JsonString(root, "DisplayName"));
            }
            catch (JsonException) { }
        }
        return games;
    }

    private static string? JsonString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void Add(List<Installation> games, string? directory, string? name)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name) || !Path.IsPathFullyQualified(directory)) return;
        try
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (path == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!)) return;
            games.Add(new(path, name));
        }
        catch (ArgumentException) { }
        catch (IOException) { }
    }

    private static string[] ManifestFiles(string directory, string pattern)
    {
        try { return Directory.EnumerateFiles(directory, pattern).Take(2048).ToArray(); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static string? ReadManifest(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length > MaximumManifestBytes) return null;
            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}