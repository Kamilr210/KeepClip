using System.Runtime.InteropServices;
using System.Text;

namespace KeepClip.Infrastructure;

// Token odświeżania Google jest chroniony przez Menedżera poświadczeń Windows
// i nigdy nie trafia jawnie do katalogu data ani repozytorium.
public static class CredentialStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2; // Zachowuje dane po wylogowaniu i restarcie tego użytkownika.
    private const int CRED_MAX_CREDENTIAL_BLOB_SIZE = 5 * 512; // 2560 bajtów.

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;            // Wartość FILETIME, pomijana podczas zapisu.
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW([In] ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);

    public static void Write(string target, string secret, string? userName = null)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        if (blob.Length > CRED_MAX_CREDENTIAL_BLOB_SIZE)
            throw new ArgumentException("Sekret jest zbyt długi dla Menedżera poświadczeń.");

        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userName ?? target,
            };
            if (!CredWriteW(ref cred, 0))
                throw new InvalidOperationException(
                    $"CredWrite nie powiodło się (kod {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static string? Read(string target)
    {
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out IntPtr ptr))
            return null; // Brak wpisu lub dostępu jest traktowany jak brak tokenu.
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                return string.Empty;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static void Delete(string target)
    {
        try { CredDeleteW(target, CRED_TYPE_GENERIC, 0); } catch { }
    }
}
