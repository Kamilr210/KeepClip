namespace KeepClip.Domain;

/// <summary>Google Drive connection status for the cloud panel.</summary>
public record CloudStatus(
    bool Configured,
    bool Connected,
    string? AccountEmail,
    long? UsageBytes,
    long? CapacityBytes
);
