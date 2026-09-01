using System.Security.Cryptography;
using System.Text;

namespace CodexAccountTray;

public static class CredentialVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexAccountTray.v1");

    public static byte[] Protect(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
    }

    public static byte[] Unprotect(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
    }
}
