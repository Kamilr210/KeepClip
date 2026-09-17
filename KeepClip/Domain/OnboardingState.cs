namespace KeepClip.Domain;

public record OnboardingState(
    bool started,
    bool completed,
    bool skipped,
    string? current_step,
    int completed_version,
    int current_version,
    IReadOnlyList<string> seen_hints
);
