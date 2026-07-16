namespace KeepClip.Domain;

public record ReplayConfig(
    bool Enabled,
    int DurationS,
    int Fps,
    string Quality,
    string Hotkey,
    bool Mic,
    string? AudioOutput,
    string? AudioInput
);
