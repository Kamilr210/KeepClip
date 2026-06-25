using System.Runtime.InteropServices;

namespace KeepClip.Infrastructure;

/// <summary>
/// Recycle Bin deletion via the Win32 shell — the C# stand-in for Python's
/// <c>send2trash</c>. Uses <c>SHFileOperation</c> with <c>FOF_ALLOWUNDO</c> so the
/// file is recoverable (never a hard delete). Dependency-free P/Invoke keeps the
/// install footprint small and avoids pulling in extra NuGet packages.
/// </summary>
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

    /// <summary>Send a single file to the Recycle Bin. Throws <see cref="IOException"/> on failure.</summary>
    public static void Send(string path)
    {
        // pFrom must be double-null-terminated; the marshaller appends one null,
        // so a single trailing "\0" yields the required "...\0\0".
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

    /// <summary>
    /// Move a file to the Recycle Bin, retrying briefly on a sharing violation.
    /// Right after the player stops streaming a clip, Windows can keep the file
    /// handle open for a moment (WinError 32, "used by another process"); a few
    /// short retries let that handle close. Returns null on success, else the last
    /// error string. Mirrors <c>_send_to_trash_with_retry</c>.
    /// </summary>
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
