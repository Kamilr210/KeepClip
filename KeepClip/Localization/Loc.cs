using System.ComponentModel;

namespace KeepClip.Localization;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _language = Settings.GetLanguage();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage
    {
        get => _language;
        set
        {
            var norm = Config.UiLanguages.Contains(value) ? value : Config.DefaultLanguage;
            if (_language == norm) return;
            _language = norm;
            Settings.SetLanguage(norm);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }
    }

    public string this[string key] => T(key);

    private static readonly Dictionary<string, Dictionary<string, string>> Extra = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pl"] = new()
        {
            ["clipMenu.play"] = "Odtwórz",
            ["clipMenu.reveal"] = "Pokaż w Eksploratorze",
            ["clipMenu.copyPath"] = "Kopiuj ścieżkę",
            ["clipMenu.uploadCloud"] = "Wyślij do chmury",
            ["clipMenu.downloadCloud"] = "Pobierz z chmury",
            ["clipMenu.removeFromFolder"] = "Usuń z folderu",
        },
        ["en"] = new()
        {
            ["clipMenu.play"] = "Play",
            ["clipMenu.reveal"] = "Show in Explorer",
            ["clipMenu.copyPath"] = "Copy path",
            ["clipMenu.uploadCloud"] = "Upload to cloud",
            ["clipMenu.downloadCloud"] = "Download from cloud",
            ["clipMenu.removeFromFolder"] = "Remove from folder",
        },
        ["ru"] = new()
        {
            ["clipMenu.play"] = "Воспроизвести",
            ["clipMenu.reveal"] = "Показать в проводнике",
            ["clipMenu.copyPath"] = "Копировать путь",
            ["clipMenu.uploadCloud"] = "Отправить в облако",
            ["clipMenu.downloadCloud"] = "Скачать из облака",
            ["clipMenu.removeFromFolder"] = "Удалить из папки",
        },
        ["uk"] = new()
        {
            ["clipMenu.play"] = "Відтворити",
            ["clipMenu.reveal"] = "Показати у провіднику",
            ["clipMenu.copyPath"] = "Копіювати шлях",
            ["clipMenu.uploadCloud"] = "Відправити у хмару",
            ["clipMenu.downloadCloud"] = "Завантажити з хмари",
            ["clipMenu.removeFromFolder"] = "Видалити з папки",
        },
    };

    public static string T(string key)
    {
        var lang = Instance._language;
        string? raw = null;
        if (Extra.TryGetValue(lang, out var ex) && ex.TryGetValue(key, out var exVal))
            raw = exVal;
        else if (Translations.All.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var val))
            raw = val;
        else if (Translations.All.TryGetValue("en", out var en) && en.TryGetValue(key, out var enVal))
            raw = enVal;
        else if (Translations.All.TryGetValue("pl", out var pl) && pl.TryGetValue(key, out var plVal))
            raw = plVal;

        if (raw is null) return key;
        return raw.Contains('<')
            ? raw.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                 .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                 .Replace("<b>", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("</b>", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("<code>", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("</code>", "", StringComparison.OrdinalIgnoreCase)
            : raw;
    }

    public static string Format(string key, params (string Name, object? Value)[] args)
    {
        var text = T(key);
        foreach (var (name, value) in args)
            text = text.Replace($"{{{name}}}", value?.ToString() ?? "");
        return text;
    }

    public static string ClipWord(int n)
    {
        var lang = Instance._language;
        if (lang == "en") return n == 1 ? T("clipCount.one") : T("clipCount.many");
        int mod10 = n % 10, mod100 = n % 100;
        if (n == 1) return T("clipCount.one");
        if (mod10 is >= 2 and <= 4 && mod100 is not (>= 12 and <= 14)) return T("clipCount.few");
        return T("clipCount.many");
    }
}

