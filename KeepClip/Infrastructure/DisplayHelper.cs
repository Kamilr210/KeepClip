using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeepClip.Infrastructure;

internal static class DisplayHelper
{
    private static readonly Dictionary<string, string> KnownGameNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cs2"] = "Counter-Strike 2",
        };

    private static readonly HashSet<string> ReservedFolderNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);
    private const int ENUM_CURRENT_SETTINGS = -1;

    public static bool PreferGdiCapture()
    {
        if (Environment.GetEnvironmentVariable("KEEPCLIP_CAPTURE_GDI") == "1") return true;
        if (Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            if (System.Windows.Forms.SystemInformation.TerminalServerSession) return true;
        }
        catch { }

        var sessionName = Environment.GetEnvironmentVariable("SESSIONNAME");
        return sessionName?.StartsWith("RDP-", StringComparison.OrdinalIgnoreCase) == true;
    }

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
            var processName = p.ProcessName;
            if (string.IsNullOrWhiteSpace(processName)) return null;
            if (p.Id == Environment.ProcessId) return null;
            if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return null;
            if (processName.Equals("KeepClip", StringComparison.OrdinalIgnoreCase)) return null;

            var name = KnownGameNames.GetValueOrDefault(processName)
                       ?? ProductName(p)
                       ?? processName;
            return SafeFolderName(name);
        }
        catch { return null; }
    }

    private static string? ProductName(Process process)
    {
        try
        {
            var info = process.MainModule?.FileVersionInfo;
            foreach (var value in new[] { info?.ProductName, info?.FileDescription })
            {
                var name = value?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.Contains("Microsoft Windows", StringComparison.OrdinalIgnoreCase)) continue;
                return name;
            }
        }
        catch { }
        return null;
    }

    public static string SafeFolderName(string name)
    {
        var cleaned = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(c, '_');

        cleaned = cleaned.TrimEnd('.', ' ');
        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or "..") return "Pulpit";
        if (ReservedFolderNames.Contains(cleaned.Split('.')[0])) cleaned = "_" + cleaned;
        return cleaned;
    }
}
