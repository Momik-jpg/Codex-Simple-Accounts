using System.Text;
using System.Security.AccessControl;
using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class CredentialVaultTests
{
    [Fact]
    public void ProtectAndUnprotect_RestoresExactBytes()
    {
        byte[] input = Encoding.UTF8.GetBytes("{\"test\":true}");

        byte[] protectedBytes = CredentialVault.Protect(input);

        Assert.NotEqual(input, protectedBytes);
        Assert.Equal(input, CredentialVault.Unprotect(protectedBytes));
    }

    [Fact]
    public void Store_ProtectsCredentialOnDisk()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexAccountTray-{Guid.NewGuid():N}");
        try
        {
            var store = new AccountStore(new AppPaths(root));
            byte[] credential = Encoding.UTF8.GetBytes("private-token-value");

            store.Save(1, credential);

            Assert.True(store.IsLoggedIn(1));
            Assert.DoesNotContain("private-token-value", File.ReadAllText(new AppPaths(root).AccountFile(1)));
            Assert.Equal(credential, store.Load(1));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Remove_DeletesStoredCredential()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexAccountTray-{Guid.NewGuid():N}");
        try
        {
            var store = new AccountStore(new AppPaths(root));
            store.Save(1, Encoding.UTF8.GetBytes("private-token-value"));

            store.Remove(1);

            Assert.False(store.IsLoggedIn(1));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void EnsureCreated_DisablesInheritedAccessRules()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexAccountTray-{Guid.NewGuid():N}");
        try
        {
            var paths = new AppPaths(root);

            paths.EnsureCreated();

            DirectorySecurity security = new DirectoryInfo(root).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
