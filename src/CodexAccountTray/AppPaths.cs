using System.Security.AccessControl;
using System.Security.Principal;

namespace CodexAccountTray;

public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(WindowsUserProfile.LocalAppData(), "CodexAccountTray");
    }

    public string Root { get; }

    public string SharedCodexHome => Path.Combine(Root, "SharedCodexHome");

    public string Accounts => Path.Combine(Root, "Accounts");

    public string AccountFile(int number) => Path.Combine(Accounts, $"account-{number}.dat");

    public string LoginHome(int number) => Path.Combine(Root, "Login", number.ToString());

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        ProtectForCurrentUser(Root);
        Directory.CreateDirectory(SharedCodexHome);
        Directory.CreateDirectory(Accounts);
        Directory.CreateDirectory(Path.Combine(Root, "Login"));
    }

    private static void ProtectForCurrentUser(string path)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User
            ?? throw new InvalidOperationException("Das aktuelle Windows-Konto konnte nicht bestimmt werden.");
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
