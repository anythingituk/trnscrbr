using System.Runtime.InteropServices;
using System.Text;

namespace Trnscrbr.Services;

public sealed class CredentialStore
{
    private const string TargetName = "Trnscrbr/OpenAI";
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    public bool HasOpenAiApiKey()
    {
        return ReadOpenAiApiKey() is { Length: > 0 };
    }

    public string? ReadOpenAiApiKey()
    {
        if (!CredRead(TargetName, CRED_TYPE_GENERIC, 0, out var credentialPointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void SaveOpenAiApiKey(string apiKey)
    {
        var secret = Encoding.Unicode.GetBytes(apiKey);
        var credential = new Credential
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = TargetName,
            CredentialBlobSize = secret.Length,
            CredentialBlob = Marshal.StringToCoTaskMemUni(apiKey),
            Persist = CRED_PERSIST_LOCAL_MACHINE,
            UserName = Environment.UserName
        };

        try
        {
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Credential Manager write failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
        }
    }

    public void DeleteOpenAiApiKey()
    {
        CredDelete(TargetName, CRED_TYPE_GENERIC, 0);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref Credential userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}
