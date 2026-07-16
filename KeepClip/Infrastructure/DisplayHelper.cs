using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeepClip.Infrastructure;

internal static class DisplayHelper
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }

    // EnumDisplaySettings zwraca fizyczne piksele niezależnie od skalowania DPI.
    // Rozmiar kodowania musi być parzysty i mieścić się w limicie H.264 NVENC.
    public static (int grabW, int grabH, int capW, int capH) MeasurePrimary()
    {
        int w = 0, h = 0;
        try
        {
            var name = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (name is not null && EnumDisplaySettingsW(name, ENUM_CURRENT_SETTINGS, ref dm))
            {
                w = (int)dm.dmPelsWidth;
                h = (int)dm.dmPelsHeight;
            }
        }
        catch { }
        if (w < 320 || h < 240) { w = 1920; h = 1080; }
        int grabW = w, grabH = h;

        if (w > 3840) { h = (int)Math.Round(h * 3840.0 / w); w = 3840; }
        return (grabW, grabH, w & ~1, h & ~1);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static string? ForegroundGameName()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (p.Id == Environment.ProcessId) return null;
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return null;
            if (name.Equals("KeepClip", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
        catch { return null; }
    }
}
