using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace SupportToolkit.Core.Secrets;

/// <summary>
/// Stores SupportToolkit secrets as Generic Credentials in Windows Credential
/// Manager for the current Windows user.
///
/// Windows Credential Manager is an implementation detail behind ISecretStore.
/// </summary>
public sealed class WindowsCredentialStore
    : ISecretStore
{
    private const uint CredentialTypeGeneric =
        1;

    private const uint CredentialPersistLocalMachine =
        2;

    private const int ErrorNotFound =
        1168;

    private const int MaximumCredentialBlobBytes =
        2560;

    private const string TargetPrefix =
        "SupportToolkit:";

    public Task<string?> GetSecretAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWindows();

        var targetName =
            BuildTargetName(
                key
            );

        if (!CredRead(
                targetName,
                CredentialTypeGeneric,
                0,
                out var credentialPointer))
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error == ErrorNotFound)
            {
                return Task.FromResult<string?>(
                    null
                );
            }

            throw CreateCredentialException(
                "read",
                error
            );
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<NativeCredential>(
                    credentialPointer
                );

            if (credential.CredentialBlob == IntPtr.Zero
                || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(
                    null
                );
            }

            var bytes =
                new byte[
                    credential.CredentialBlobSize
                ];

            try
            {
                Marshal.Copy(
                    credential.CredentialBlob,
                    bytes,
                    0,
                    bytes.Length
                );

                return Task.FromResult<string?>(
                    Encoding.UTF8.GetString(
                        bytes
                    )
                );
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    bytes
                );
            }
        }
        finally
        {
            CredFree(
                credentialPointer
            );
        }
    }

    public Task SetSecretAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWindows();

        var targetName =
            BuildTargetName(
                key
            );

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Secret value cannot be empty.",
                nameof(value)
            );
        }

        var bytes =
            Encoding.UTF8.GetBytes(
                value
            );

        if (bytes.Length > MaximumCredentialBlobBytes)
        {
            CryptographicOperations.ZeroMemory(
                bytes
            );

            throw new ArgumentException(
                $"Secret exceeds the Windows Credential Manager " +
                $"limit of {MaximumCredentialBlobBytes} bytes.",
                nameof(value)
            );
        }

        var credentialBlob =
            IntPtr.Zero;

        try
        {
            credentialBlob =
                Marshal.AllocHGlobal(
                    bytes.Length
                );

            Marshal.Copy(
                bytes,
                0,
                credentialBlob,
                bytes.Length
            );

            var credential =
                new NativeCredential
                {
                    Type =
                        CredentialTypeGeneric,

                    TargetName =
                        targetName,

                    CredentialBlobSize =
                        (uint)bytes.Length,

                    CredentialBlob =
                        credentialBlob,

                    Persist =
                        CredentialPersistLocalMachine,

                    UserName =
                        "SupportToolkit"
                };

            if (!CredWrite(
                    ref credential,
                    0))
            {
                throw CreateCredentialException(
                    "write",
                    Marshal.GetLastWin32Error()
                );
            }
        }
        finally
        {
            if (credentialBlob != IntPtr.Zero)
            {
                var zeroes =
                    new byte[
                        bytes.Length
                    ];

                Marshal.Copy(
                    zeroes,
                    0,
                    credentialBlob,
                    zeroes.Length
                );

                Marshal.FreeHGlobal(
                    credentialBlob
                );
            }

            CryptographicOperations.ZeroMemory(
                bytes
            );
        }

        return Task.CompletedTask;
    }

    public Task RemoveSecretAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWindows();

        var targetName =
            BuildTargetName(
                key
            );

        if (!CredDelete(
                targetName,
                CredentialTypeGeneric,
                0))
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error == ErrorNotFound)
            {
                return Task.CompletedTask;
            }

            throw CreateCredentialException(
                "delete",
                error
            );
        }

        return Task.CompletedTask;
    }

    private static string BuildTargetName(
        string key)
    {
        if (string.IsNullOrWhiteSpace(
                key))
        {
            throw new ArgumentException(
                "Secret key cannot be empty.",
                nameof(key)
            );
        }

        return
            TargetPrefix
            + key.Trim();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The Windows Credential Manager secret store " +
                "is available only on Windows."
            );
        }
    }

    private static Exception CreateCredentialException(
        string operation,
        int error)
    {
        var windowsMessage =
            new Win32Exception(
                error
            ).Message;

        return new InvalidOperationException(
            $"Windows Credential Manager could not {operation} " +
            $"the SupportToolkit credential. " +
            $"Windows error {error}: {windowsMessage}"
        );
    }

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPointer
    );

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags
    );

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags
    );

    [DllImport(
        "advapi32.dll")]
    private static extern void CredFree(
        IntPtr buffer
    );

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;

        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public FILETIME LastWritten;

        public uint CredentialBlobSize;

        public IntPtr CredentialBlob;

        public uint Persist;

        public uint AttributeCount;

        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }
}