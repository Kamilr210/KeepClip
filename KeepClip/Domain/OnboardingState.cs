namespace KeepClip.Domain;

// Stan samouczka. Nazwy pól trafiają wprost do JSON-a, dlatego zapisane są
// z podkreśleniami — tak jak reszta interfejsu programistycznego.
public record OnboardingState(
    bool started,
    bool completed,
    bool skipped,
    string? current_step,
    int completed_version,
    int current_version,
    IReadOnlyList<string> seen_hints
);
