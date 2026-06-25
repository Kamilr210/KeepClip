namespace KeepClip.Domain;

/// <summary>Persisted instant-replay configuration (the editable settings the UI sends).
/// The live runtime status (running/saving/encoder/…) is reported separately by the
/// replay service, which has more transient fields than this config record.</summary>
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
