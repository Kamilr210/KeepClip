using System.Runtime.InteropServices;

namespace KeepClip.Infrastructure;

// Rozmiar pamięci karty graficznej decyduje o doborze modelu transkrypcji. DXGI podaje go
// dla kart NVIDIA, AMD i Intel, w odróżnieniu od WMI, gdzie AdapterRAM jest 32-bitowe
// i zatrzymuje się na 4 GB.
internal static class GpuInfo
{
    private const uint AdapterFlagSoftware = 2;
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);

    // Zwraca pamięć najmocniejszej karty albo 0, gdy nie da się jej ustalić.
    public static long LargestDedicatedVideoMemory()
    {
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(in iid, out var factoryPtr) < 0 || factoryPtr == IntPtr.Zero)
                return 0;

            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.Release(factoryPtr);
            try
            {
                long best = 0;
                for (uint i = 0; ; i++)
                {
                    int hr = factory.EnumAdapters1(i, out var adapter);
                    if (hr == DxgiErrorNotFound || hr < 0 || adapter is null) break;
                    try
                    {
                        if (adapter.GetDesc1(out var desc) >= 0 &&
                            (desc.Flags & AdapterFlagSoftware) == 0)
                            best = Math.Max(best, (long)(ulong)desc.DedicatedVideoMemory);
                    }
                    finally { Marshal.ReleaseComObject(adapter); }
                }
                return best;
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch
        {
            // Brak DXGI nie może blokować transkrypcji — wyżej zadziała ostrożny wariant.
            return 0;
        }
    }

    public static string Describe(long bytes) =>
        bytes > 0 ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB VRAM" : "nieznana pamięć GPU";

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

    // Metody przed tymi używanymi muszą zostać zadeklarowane, aby zgadzały się pozycje
    // w tablicy wirtualnej COM; nigdy nie są wywoływane.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint adapter, out IDXGIAdapter1 ppAdapter);
        [PreserveSig] bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }
}
