#if KEEPCLIP_DEV
using System.Globalization;
using System.Runtime.InteropServices;

namespace KeepClip.Infrastructure;

internal sealed class DevGpuCounters : IDisposable
{
    internal sealed record Engine(int Pid, string Key, double Usage);
    internal sealed record Reading(IReadOnlyList<Engine>? Engines, IReadOnlyDictionary<int, long>? Dedicated, IReadOnlyDictionary<int, long>? Shared)
    {
        public double? TotalUsage => Engines is null ? null : BusiestEngine(Engines);
        public double? ProcessUsage(int pid) => Engines is null ? null : BusiestEngine(Engines.Where(e => e.Pid == pid));
        public long? DedicatedBytes(int pid) => Dedicated is null ? null : Dedicated.GetValueOrDefault(pid);
        public long? SharedBytes(int pid) => Shared is null ? null : Shared.GetValueOrDefault(pid);
    }

    private IntPtr _query, _engine, _dedicated, _shared;

    public Reading Read(HashSet<int> pids, bool reset)
    {
        if (reset) Dispose();
        var first = _query == IntPtr.Zero;
        if (first)
        {
            if (PdhOpenQueryW(null, 0, out _query) != 0) { _query = IntPtr.Zero; return new(null, null, null); }
            _engine = Add(@"\GPU Engine(*)\Utilization Percentage");
            _dedicated = Add(@"\GPU Process Memory(*)\Dedicated Usage");
            _shared = Add(@"\GPU Process Memory(*)\Shared Usage");
        }
        if (PdhCollectQueryData(_query) != 0) return new(null, null, null);
        var engines = first ? null : Values(_engine);
        var rows = engines?.Where(v => TryPid(v.Name, out var pid, out _) && pids.Contains(pid)).Select(v =>
        {
            TryPid(v.Name, out var pid, out var key);
            return new Engine(pid, key, v.Value);
        }).ToArray();
        return new Reading(rows, Memory(_dedicated, pids), Memory(_shared, pids));
    }

    internal static double BusiestEngine(IEnumerable<Engine> engines) => Math.Clamp(engines.GroupBy(e => e.Key).Select(group => group.Sum(e => Math.Max(0, e.Usage))).DefaultIfEmpty(0).Max(), 0, 100);

    internal static bool TryPid(string name, out int pid, out string key)
    {
        pid = 0;
        key = "";
        if (!name.StartsWith("pid_", StringComparison.Ordinal)) return false;
        var end = name.IndexOf('_', 4);
        if (end < 0 || !int.TryParse(name.AsSpan(4, end - 4), NumberStyles.None, CultureInfo.InvariantCulture, out pid)) return false;
        key = name[(end + 1)..];
        return true;
    }

    private IntPtr Add(string path) => PdhAddEnglishCounterW(_query, path, 0, out var counter) == 0 ? counter : IntPtr.Zero;

    private static Dictionary<int, long>? Memory(IntPtr counter, HashSet<int> pids)
    {
        var values = Values(counter);
        if (values is null) return null;
        var result = new Dictionary<int, long>();
        foreach (var value in values)
            if (TryPid(value.Name, out var pid, out _) && pids.Contains(pid)) result[pid] = result.GetValueOrDefault(pid) + (long)Math.Max(0, value.Value);
        return result;
    }

    private static List<(string Name, double Value)>? Values(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        const uint format = 0x200 | 0x8000;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            uint size = 0;
            var status = PdhGetFormattedCounterArrayW(counter, format, ref size, out var count, IntPtr.Zero);
            if (status == 0 && count == 0) return new();
            if (status != 0x800007d2 || size == 0 || size > 32 * 1024 * 1024) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, format, ref size, out count, buffer);
                if (status == 0x800007d2) continue;
                if (status != 0) return null;
                var result = new List<(string, double)>();
                var stride = Marshal.SizeOf<CounterItem>();
                if (count > size / stride) return null;
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * stride));
                    if (item.Value.Status > 1 || !double.IsFinite(item.Value.Number)) continue;
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (name is not null) result.Add((name, item.Value.Number));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = _engine = _dedicated = _shared = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Number;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhOpenQueryW(string? source, nuint userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, nuint userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
#endif
