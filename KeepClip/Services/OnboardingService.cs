using System.Text.Json.Nodes;

namespace KeepClip.Services;

// Samouczek przechowuje stan w settings.json, w jednym obiekcie "onboarding".
// Nie ma własnej tabeli, bo to kilka pól czytanych raz przy starcie interfejsu.
public static class OnboardingService
{
    // Podbicie tej liczby NIE uruchamia samouczka ponownie u dotychczasowych
    // użytkowników — służy migracjom i temu, żeby wiedzieć, którą wersję ktoś przeszedł.
    // Nowe pojedyncze funkcje pokazuje mechanizm podpowiedzi (seen_hints).
    public const int CurrentVersion = 1;

    private const string Key = "onboarding";
    private const int MaxSeenHints = 200;   // Lista widzianych podpowiedzi nie może rosnąć bez końca.
    private static readonly object Gate = new();

    public static OnboardingState Get()
    {
        lock (Gate) return Read();
    }

    public static OnboardingState Patch(
        bool? started, bool? completed, bool? skipped, string? currentStep, string? seenHint)
    {
        lock (Gate)
        {
            var s = Read();
            var isStarted = started ?? s.started;
            var isCompleted = completed ?? s.completed;
            var isSkipped = skipped ?? s.skipped;
            var step = string.IsNullOrWhiteSpace(currentStep) ? s.current_step : currentStep.Trim();
            var completedVersion = s.completed_version;

            if (isCompleted)
            {
                completedVersion = CurrentVersion;
                isSkipped = false;      // Ukończenie unieważnia wcześniejsze pominięcie.
                step = null;            // Nie ma czego wznawiać.
            }
            else if (isSkipped)
            {
                step = null;
            }

            var hints = s.seen_hints.ToList();
            var hint = seenHint?.Trim();
            if (!string.IsNullOrEmpty(hint) && !hints.Contains(hint))
            {
                hints.Add(hint);
                if (hints.Count > MaxSeenHints) hints.RemoveRange(0, hints.Count - MaxSeenHints);
            }

            return Write(new OnboardingState(
                isStarted, isCompleted, isSkipped, step, completedVersion, CurrentVersion, hints));
        }
    }

    // Ręczne uruchomienie z ustawień nie kasuje informacji, że ktoś już przeszedł samouczek.
    public static OnboardingState Restart()
    {
        lock (Gate)
        {
            var s = Read();
            return Write(s with { started = true, skipped = false, current_step = null });
        }
    }

    private static OnboardingState Read()
    {
        var o = Settings.GetObject(Key);
        return new OnboardingState(
            Bool(o, "started"),
            Bool(o, "completed"),
            Bool(o, "skipped"),
            Text(o, "current_step"),
            Number(o, "completed_version"),
            CurrentVersion,
            TextList(o, "seen_hints"));
    }

    private static OnboardingState Write(OnboardingState s)
    {
        var hints = new JsonArray();
        foreach (var h in s.seen_hints) hints.Add(h);
        Settings.SetObject(Key, new JsonObject
        {
            ["started"] = s.started,
            ["completed"] = s.completed,
            ["skipped"] = s.skipped,
            ["current_step"] = s.current_step,
            ["completed_version"] = s.completed_version,
            ["seen_hints"] = hints,
        });
        return s;
    }

    private static bool Bool(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? Text(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v &&
           v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s) ? s : null;

    private static int Number(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

    private static List<string> TextList(JsonObject o, string key)
    {
        var list = new List<string>();
        if (o.TryGetPropertyValue(key, out var n) && n is JsonArray arr)
            foreach (var item in arr)
                if (item is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                    list.Add(s);
        return list;
    }
}
