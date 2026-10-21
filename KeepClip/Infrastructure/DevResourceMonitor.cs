#if KEEPCLIP_DEV
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeepClip.Infrastructure;

internal sealed class DevResourceMonitor : IDisposable
{
    internal sealed record ProcessEntry(int Id, int ParentId, string Name);
    internal sealed record ProcessSample(long Started, long CpuTicks, long? RamBytes, ulong? ReadBytes, ulong? WriteBytes);

    private static Dictionary<int, string> _webViewProcesses = new();
    private readonly object _gate = new();
    private readonly DevGpuCounters _gpu = new();
    private Dictionary<int, ProcessSample> _previous = new();
    private long _lastTimestamp;
    private ResourceSnapshot? _lastSnapshot;

    public static void SetWebViewProcesses(Dictionary<int, string> processes) => Volatile.Write(ref _webViewProcesses, processes);

    public ResourceSnapshot Sample()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = _lastTimestamp == 0 ? 0 : (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            if (_lastSnapshot is not null && elapsed < 0.5) return _lastSnapshot;
            var warmup = elapsed <= 0 || elapsed > 10;
            if (warmup) _previous.Clear();

            var webView = Volatile.Read(ref _webViewProcesses);
            var entries = ReadProcessTree();
            var selected = SelectProcesses(Environment.ProcessId, entries, webView);
            var names = entries.ToDictionary(p => p.Id, p => p.Name);
            var samples = new Dictionary<int, ProcessSample>();
            foreach (var pid in selected.Keys)
            {
                var sample = ReadProcess(pid);
                if (sample is not null) samples[pid] = sample;
            }
            var gpu = _gpu.Read(samples.Keys.ToHashSet(), warmup);
            var processors = (int)GetActiveProcessorCount(ushort.MaxValue);
            if (processors <= 0) processors = Environment.ProcessorCount;
            var rows = new List<ResourceProcess>();
            foreach (var (pid, sample) in samples)
            {
                _previous.TryGetValue(pid, out var previous);
                var cpu = CpuPercent(sample, previous, elapsed, processors);
                var read = Rate(sample, previous, elapsed, false);
                var write = Rate(sample, previous, elapsed, true);
                var usage = new ResourceUsage(cpu, sample.RamBytes, gpu.ProcessUsage(pid), gpu.DedicatedBytes(pid), gpu.SharedBytes(pid), read, write);
                rows.Add(new ResourceProcess(pid, names.GetValueOrDefault(pid, "KeepClip"), selected[pid], usage));
            }

            var total = new ResourceUsage(Sum(rows, r => r.cpu_percent), SumBytes(rows, r => r.ram_bytes), gpu.TotalUsage, SumBytes(rows, r => r.gpu_dedicated_bytes), SumBytes(rows, r => r.gpu_shared_bytes), Sum(rows, r => r.read_bytes_per_second), Sum(rows, r => r.write_bytes_per_second));
            var single = SingleOperationProgress.Snapshot();
            var transcribing = TranscribeState.Snapshot().running || (single["active"] is true && single["kind"] as string == "transcription");
            _lastTimestamp = now;
            _previous = samples;
            _lastSnapshot = new ResourceSnapshot(DateTimeOffset.UtcNow, warmup ? 0 : elapsed, warmup, transcribing, webView.Count > 0, selected.Count - rows.Count, total, rows.OrderBy(r => r.pid == Environment.ProcessId ? 0 : r.kind.StartsWith("WebView2", StringComparison.Ordinal) ? 1 : 2).ThenBy(r => r.pid).ToArray());
            return _lastSnapshot;
        }
    }

    private static double? Sum(List<ResourceProcess> rows, Func<ResourceUsage, double?> value) => rows.Count > 0 && rows.All(r => value(r.usage).HasValue) ? rows.Sum(r => value(r.usage)!.Value) : null;
    private static long? SumBytes(List<ResourceProcess> rows, Func<ResourceUsage, long?> value) => rows.Count > 0 && rows.All(r => value(r.usage).HasValue) ? rows.Sum(r => value(r.usage)!.Value) : null;

    internal static Dictionary<int, string> SelectProcesses(int root, IReadOnlyList<ProcessEntry> entries, IReadOnlyDictionary<int, string> webView)
    {
        var selected = new Dictionary<int, string> { [root] = "KeepClip / Whisper" };
        foreach (var entry in entries)
            if (IsWebView(entry.Name) && webView.TryGetValue(entry.Id, out var kind)) selected[entry.Id] = "WebView2 / " + kind;
        bool changed;
        do
        {
            changed = false;
            foreach (var entry in entries)
            {
                if (selected.ContainsKey(entry.Id) || !selected.ContainsKey(entry.ParentId)) continue;
                var kind = IsWebView(entry.Name) ? "WebView2" : entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ? "FFmpeg" : entry.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase) ? "FFprobe" : null;
                if (kind is null) continue;
                selected[entry.Id] = kind;
                changed = true;
            }
        } while (changed);
        return selected;
    }

    private static bool IsWebView(string name) => name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("crashpad_handler.exe", StringComparison.OrdinalIgnoreCase);

    internal static double? CpuPercent(ProcessSample current, ProcessSample? previous, double seconds, int processors)
    {
        if (previous is null || current.Started != previous.Started || seconds <= 0 || processors <= 0 || current.CpuTicks < previous.CpuTicks) return null;
        return Math.Clamp((current.CpuTicks - previous.CpuTicks) / (double)TimeSpan.TicksPerSecond / seconds / processors * 100, 0, 100);
    }

    internal static double? Rate(ProcessSample current, ProcessSample? previous, double seconds, bool write)
    {
        if (previous is null || current.Started != previous.Started || seconds <= 0) return null;
        var value = write ? current.WriteBytes : current.ReadBytes;
        var old = write ? previous.WriteBytes : previous.ReadBytes;
        return value.HasValue && old.HasValue && value >= old ? (value.Value - old.Value) / seconds : null;
    }

    private static List<ProcessEntry> ReadProcessTree()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        var entries = new List<ProcessEntry>();
        if (snapshot.IsInvalid) return entries;
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32FirstW(snapshot, ref entry)) return entries;
        do { entries.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile)); }
        while (Process32NextW(snapshot, ref entry));
        return entries;
    }

    private static ProcessSample? ReadProcess(int pid)
    {
        using var handle = OpenProcess(0x0410, false, pid);
        if (handle.IsInvalid || !GetProcessTimes(handle, out var started, out _, out var kernel, out var user)) return null;
        var memory = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        long? ram = GetProcessMemoryInfo(handle, ref memory, memory.Size) ? (long)memory.PrivateWorkingSetSize : null;
        var hasIo = GetProcessIoCounters(handle, out var io);
        return new ProcessSample(started, kernel + user, ram, hasIo ? io.ReadTransferCount : null, hasIo ? io.WriteTransferCount : null);
    }

    public void Dispose() { lock (_gate) _gpu.Dispose(); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage, PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort groupNumber);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("psapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessMemoryInfo(SafeProcessHandle process, ref MemoryCounters memory, uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessIoCounters(SafeProcessHandle process, out IoCounters counters);
}
#endif
