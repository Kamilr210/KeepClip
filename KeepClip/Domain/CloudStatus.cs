namespace KeepClip.Domain;

public record CloudStatus(
    bool Configured,
    bool Connected,
    string? AccountEmail,
    long? UsageBytes,
    long? CapacityBytes
);
