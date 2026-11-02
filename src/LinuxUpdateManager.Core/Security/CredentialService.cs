using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace LinuxUpdateManager.Core.Security;

public sealed class CredentialService
{
    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const uint ErrorNotFound = 1168;
    private const int MaximumCredentialBlobSize = 5 * 512;

    public static string GetTargetName(Guid serverId, CredentialKind kind)
    {
        if (serverId == Guid.Empty || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("A valid server identifier and credential kind are required.");
        }

        return $"LinuxUpdateManager/{serverId:N}/{kind}";
    }

    public void Save(Guid serverId, CredentialKind kind, string secret)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var byteCount = Encoding.Unicode.GetByteCount(secret);
        if (byteCount > MaximumCredentialBlobSize)
        {
            throw new ArgumentOutOfRangeException(nameof(secret), "The credential exceeds the Windows Credential Manager size limit.");
        }

        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = GetTargetName(serverId, kind),
                CredentialBlobSize = (uint)byteCount,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = string.Empty
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the credential.");
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public string? Retrieve(Guid serverId, CredentialKind kind)
    {
        EnsureWindows();
        if (!CredRead(GetTargetName(serverId, kind), GenericCredentialType, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Windows Credential Manager could not retrieve the credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlobSize > MaximumCredentialBlobSize || credential.CredentialBlobSize % 2 != 0)
            {
                throw new SecurityException("Windows Credential Manager returned a credential with an invalid size.");
            }

            if (credential.CredentialBlobSize > 0 && credential.CredentialBlob == IntPtr.Zero)
            {
                throw new SecurityException("Windows Credential Manager returned an empty credential buffer.");
            }

            return credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally
        {
            FreeCredentialBuffer(credentialPointer);
        }
    }

    public bool Exists(Guid serverId, CredentialKind kind)
    {
        EnsureWindows();
        if (!CredRead(GetTargetName(serverId, kind), GenericCredentialType, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return false;
            }

            throw new Win32Exception(error, "Windows Credential Manager could not check the credential.");
        }

        FreeCredentialBuffer(credentialPointer);
        return true;
    }

    public bool Delete(Guid serverId, CredentialKind kind)
    {
        EnsureWindows();
        if (CredDelete(GetTargetName(serverId, kind), GenericCredentialType, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(error, "Windows Credential Manager could not delete the credential.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Credentials are available only on Windows.");
        }
    }

    private static void FreeCredentialBuffer(IntPtr credentialPointer)
    {
        if (credentialPointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob != IntPtr.Zero &&
                credential.CredentialBlobSize <= MaximumCredentialBlobSize)
            {
                for (var index = 0; index < credential.CredentialBlobSize; index++)
                {
                    Marshal.WriteByte(credential.CredentialBlob, checked((int)index), 0);
                }
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
