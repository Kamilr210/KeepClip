using System.Runtime.InteropServices;

namespace KeepClip.Infrastructure;

// SHFileOperation z FOF_ALLOWUNDO przenosi plik do Kosza zamiast usuwać go trwale.
public static class Trash
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    public static void Send(string path)
    {
        // Mechanizm konwersji struktur dodaje drugi znak końca wymagany przez pFrom.
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };
        int rc = SHFileOperation(ref op);
        if (rc != 0 || op.fAnyOperationsAborted)
            throw new IOException($"Nie udało się przenieść do Kosza (kod {rc}).");
    }

    // Po zamknięciu odtwarzacza uchwyt pliku może pozostać chwilowo otwarty,
    // dlatego błąd współdzielenia jest ponawiany przez krótki czas.
    public static string? SendWithRetry(string path, int attempts = 6, int delayMs = 250)
    {
        string? last = null;
        for (int i = 0; i < attempts; i++)
        {
            try { Send(path); return null; }
            catch (Exception ex)
            {
                last = ex.Message;
                if (i < attempts - 1) Thread.Sleep(delayMs);
            }
        }
        return last;
    }
}
