#if KEEPCLIP_DEV
namespace KeepClip.Domain;

internal sealed record ResourceUsage(double? cpu_percent, long? ram_bytes, double? gpu_percent, long? gpu_dedicated_bytes, long? gpu_shared_bytes, double? read_bytes_per_second, double? write_bytes_per_second);

internal sealed record ResourceProcess(int pid, string name, string kind, ResourceUsage usage);

internal sealed record ResourceSnapshot(DateTimeOffset captured_at, double sample_seconds, bool warming_up, bool transcribing, bool webview_attached, int unreadable_processes, ResourceUsage total, IReadOnlyList<ResourceProcess> processes);
#endif
