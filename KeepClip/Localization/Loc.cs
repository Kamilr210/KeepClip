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
            ["player.fullscreen"] = "Pełny ekran (F)",
            ["player.exitFullscreen"] = "Wyjdź z pełnego ekranu (F / Esc)",
            ["player.theater"] = "Tryb kinowy (T)",
            ["player.exitTheater"] = "Zwykły widok (T)",
            ["player.speed"] = "Prędkość odtwarzania",
            ["player.speedNormal"] = "Normalna",
            ["player.play"] = "Odtwórz (Spacja)",
            ["player.pause"] = "Pauza (Spacja)",
            ["player.mute"] = "Wycisz (M)",
            ["player.unmute"] = "Włącz dźwięk (M)",
            ["player.cc"] = "Napisy (C)",
            ["player.rewind5"] = "Cofnij o 5 s (←)",
            ["player.forward5"] = "Przewiń o 5 s (→)",
            ["cutOverlay.setStartHere"] = "Ustaw tutaj",
            ["cutOverlay.setEndHere"] = "Ustaw tutaj",
            ["cutOverlay.videoTrack"] = "Wideo",
            ["cutOverlay.audioTrack"] = "Dźwięk",
            ["cutOverlay.audioVolume"] = "Głośność ścieżki audio",
            ["cutOverlay.audioMuted"] = "Wyciszono (bez dźwięku)",
            ["busy.pleaseWait"] = "Proszę czekać...",
            ["busy.switchingLanguage"] = "Zmiana języka interfejsu...",
            ["busy.startingTranscribe"] = "Uruchamianie transkrypcji...",
            ["busy.scanning"] = "Skanowanie folderu z klipami...",
            ["busy.syncing"] = "Synchronizacja z chmurą...",
            ["busy.processing"] = "Przetwarzanie klipu...",
        },
        ["en"] = new()
        {
            ["clipMenu.play"] = "Play",
            ["clipMenu.reveal"] = "Show in Explorer",
            ["clipMenu.copyPath"] = "Copy path",
            ["clipMenu.uploadCloud"] = "Upload to cloud",
            ["clipMenu.downloadCloud"] = "Download from cloud",
            ["clipMenu.removeFromFolder"] = "Remove from folder",
            ["player.fullscreen"] = "Fullscreen (F)",
            ["player.exitFullscreen"] = "Exit fullscreen (F / Esc)",
            ["player.theater"] = "Theater mode (T)",
            ["player.exitTheater"] = "Default view (T)",
            ["player.speed"] = "Playback speed",
            ["player.speedNormal"] = "Normal",
            ["player.play"] = "Play (Space)",
            ["player.pause"] = "Pause (Space)",
            ["player.mute"] = "Mute (M)",
            ["player.unmute"] = "Unmute (M)",
            ["player.cc"] = "Subtitles (C)",
            ["player.rewind5"] = "Back 5s (←)",
            ["player.forward5"] = "Forward 5s (→)",
            ["cutOverlay.setStartHere"] = "Set here",
            ["cutOverlay.setEndHere"] = "Set here",
            ["cutOverlay.videoTrack"] = "Video",
            ["cutOverlay.audioTrack"] = "Audio",
            ["cutOverlay.audioVolume"] = "Audio track volume",
            ["cutOverlay.audioMuted"] = "Muted (no audio)",
            ["busy.pleaseWait"] = "Please wait...",
            ["busy.switchingLanguage"] = "Switching interface language...",
            ["busy.startingTranscribe"] = "Starting transcription...",
            ["busy.scanning"] = "Scanning clips folder...",
            ["busy.syncing"] = "Syncing with cloud...",
            ["busy.processing"] = "Processing clip...",
        },
        ["ru"] = new()
        {
            ["clipMenu.play"] = "Воспроизвести",
            ["clipMenu.reveal"] = "Показать в проводнике",
            ["clipMenu.copyPath"] = "Копировать путь",
            ["clipMenu.uploadCloud"] = "Отправить в облако",
            ["clipMenu.downloadCloud"] = "Скачать из облака",
            ["clipMenu.removeFromFolder"] = "Удалить из папки",
            ["player.fullscreen"] = "Во весь экран (F)",
            ["player.exitFullscreen"] = "Выйти из полноэкранного режима (F / Esc)",
            ["player.theater"] = "Широкий экран (T)",
            ["player.exitTheater"] = "Обычный вид (T)",
            ["player.speed"] = "Скорость воспроизведения",
            ["player.speedNormal"] = "Обычная",
            ["player.play"] = "Воспроизвести (Пробел)",
            ["player.pause"] = "Пауза (Пробел)",
            ["player.mute"] = "Выключить звук (M)",
            ["player.unmute"] = "Включить звук (M)",
            ["player.cc"] = "Субтитры (C)",
            ["player.rewind5"] = "Назад на 5 с (←)",
            ["player.forward5"] = "Вперёд на 5 с (→)",
            ["cutOverlay.setStartHere"] = "Отсюда",
            ["cutOverlay.setEndHere"] = "Досюда",
            ["cutOverlay.videoTrack"] = "Видео",
            ["cutOverlay.audioTrack"] = "Аудио",
            ["cutOverlay.audioVolume"] = "Громкость аудиодорожки",
            ["cutOverlay.audioMuted"] = "Без звука (выкл.)",
            ["busy.pleaseWait"] = "Пожалуйста, подождите...",
            ["busy.switchingLanguage"] = "Переключение языка интерфейса...",
            ["busy.startingTranscribe"] = "Запуск транскрибации...",
            ["busy.scanning"] = "Сканирование папки с клипами...",
            ["busy.syncing"] = "Синхронизация с облаком...",
            ["busy.processing"] = "Обработка клипа...",
        },
        ["uk"] = new()
        {
            ["clipMenu.play"] = "Відтворити",
            ["clipMenu.reveal"] = "Показати у провіднику",
            ["clipMenu.copyPath"] = "Копіювати шлях",
            ["clipMenu.uploadCloud"] = "Відправити у хмару",
            ["clipMenu.downloadCloud"] = "Завантажити з хмари",
            ["clipMenu.removeFromFolder"] = "Видалити з папки",
            ["player.fullscreen"] = "На весь екран (F)",
            ["player.exitFullscreen"] = "Вийти з повноекранного режиму (F / Esc)",
            ["player.theater"] = "Широкий екран (T)",
            ["player.exitTheater"] = "Звичайний вигляд (T)",
            ["player.speed"] = "Швидкість відтворення",
            ["player.speedNormal"] = "Звичайна",
            ["player.play"] = "Відтворити (Пробіл)",
            ["player.pause"] = "Пауза (Пробіл)",
            ["player.mute"] = "Вимкнути звук (M)",
            ["player.unmute"] = "Увімкнути звук (M)",
            ["player.cc"] = "Субтитри (C)",
            ["player.rewind5"] = "Назад на 5 с (←)",
            ["player.forward5"] = "Вперед на 5 с (→)",
            ["cutOverlay.setStartHere"] = "Звідси",
            ["cutOverlay.setEndHere"] = "Досюди",
            ["cutOverlay.videoTrack"] = "Відео",
            ["cutOverlay.audioTrack"] = "Аудіо",
            ["cutOverlay.audioVolume"] = "Гучність аудіодоріжки",
            ["cutOverlay.audioMuted"] = "Без звуку (вимк.)",
            ["busy.pleaseWait"] = "Будь ласка, зачекайте...",
            ["busy.switchingLanguage"] = "Зміна мови інтерфейсу...",
            ["busy.startingTranscribe"] = "Запуск транскрибації...",
            ["busy.scanning"] = "Сканування теки з кліпами...",
            ["busy.syncing"] = "Синхронізація з хмарою...",
            ["busy.processing"] = "Обробка кліпу...",
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

