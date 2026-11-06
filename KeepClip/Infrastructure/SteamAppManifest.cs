using System.Text;

namespace KeepClip.Infrastructure;

internal static class SteamAppManifest
{
    internal static (string Name, string InstallDirectory)? Parse(string text)
    {
        try
        {
            var reader = new Reader(text);
            if (reader.Next() is not { Value: "AppState", Symbol: false } || reader.Next() is not { Value: "{", Symbol: true }) return null;
            var fields = reader.ReadObject(0);
            if (reader.Next() is not null || !fields.TryGetValue("name", out var name) || !fields.TryGetValue("installdir", out var directory)) return null;
            if (!fields.TryGetValue("StateFlags", out var state) || !uint.TryParse(state, out var flags) || (flags & 4) == 0) return null;
            return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory) ? null : (name, directory);
        }
        catch (FormatException) { return null; }
    }

    private readonly record struct Token(string Value, bool Symbol = false);

    private sealed class Reader(string text)
    {
        private int _position;

        internal Dictionary<string, string> ReadObject(int depth)
        {
            if (depth > 16) throw new FormatException("Steam manifest is too deeply nested.");
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (Next() is { } key)
            {
                if (key is { Value: "}", Symbol: true }) return fields;
                if (key.Symbol || Next() is not { } value) throw new FormatException("Incomplete Steam manifest entry.");
                if (value is { Value: "{", Symbol: true }) { ReadObject(depth + 1); continue; }
                if (value.Symbol || !fields.TryAdd(key.Value, value.Value)) throw new FormatException("Invalid Steam manifest entry.");
            }
            throw new FormatException("Unclosed Steam manifest object.");
        }

        internal Token? Next()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]) || text[_position] == '\uFEFF') { _position++; continue; }
                if (text[_position] == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                {
                    while (_position < text.Length && text[_position] != '\n') _position++;
                    continue;
                }
                break;
            }
            if (_position == text.Length) return null;
            var first = text[_position++];
            if (first is '{' or '}') return new(first.ToString(), true);
            if (first != '"')
            {
                int start = _position - 1;
                while (_position < text.Length && !char.IsWhiteSpace(text[_position]) && text[_position] is not ('{' or '}')) _position++;
                return new(text[start.._position]);
            }

            var value = new StringBuilder();
            while (_position < text.Length)
            {
                var c = text[_position++];
                if (c == '"') return new(value.ToString());
                if (c == '\\' && _position < text.Length)
                {
                    var escaped = text[_position];
                    if (escaped is '\\' or '"' or 'n' or 'r' or 't')
                    {
                        _position++;
                        c = escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped };
                    }
                }
                value.Append(c);
            }
            throw new FormatException("Unclosed Steam manifest string.");
        }
    }
}