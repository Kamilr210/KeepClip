using System.Runtime.InteropServices;
using System.Text;

namespace KeepClip;

/// <summary>
/// Secure secret storage backed by the Windows Credential Manager (the OS keychain),
/// via the Win32 <c>Cred*</c> API. We keep the Google Drive <b>refresh token</b> here —
/// never in plaintext on disk, never in <c>data/</c>, never in the repo. The token is a
/// long-lived credential that can mint Drive access tokens, so it gets the same OS-level
/// protection (per-user, DPAPI-encrypted at rest) as a saved password.
///
/// Dependency-free P/Invoke, matching the project's "small install, no extra NuGet"
/// philosophy (same approach as <see cref="Trash"/>).
/// </summary>
public static class CredentialStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2; // survives logoff/reboot, this user
    private const int CRED_MAX_CREDENTIAL_BLOB_SIZE = 5 * 512; // 2560 bytes

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;            // FILETIME (ignored on write)
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

    /// <summary>
    /// Store (or overwrite) a UTF-16 secret under <paramref name="target"/>.
    /// <paramref name="userName"/> is cosmetic metadata (shown in the Credential Manager
    /// UI) — we pass the connected account's e-mail so the entry is self-describing.
    /// </summary>
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

    /// <summary>Read the secret stored under <paramref name="target"/>, or null if absent.</summary>
    public static string? Read(string target)
    {
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out IntPtr ptr))
            return null; // not found (or access denied) → treat as "no token"
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

    /// <summary>Delete the secret under <paramref name="target"/>. No-op if it isn't there.</summary>
    public static void Delete(string target)
    {
        try { CredDeleteW(target, CRED_TYPE_GENERIC, 0); } catch { /* already gone */ }
    }
}
