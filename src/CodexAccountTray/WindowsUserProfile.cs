using Microsoft.Win32;
using System.Security.Principal;

namespace CodexAccountTray;

public static class WindowsUserProfile
{
    public static string ProfilePath()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value
            ?? throw new InvalidOperationException("Das aktuelle Windows-Konto konnte nicht bestimmt werden.");
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        string? profile = key?.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new InvalidOperationException("Das Windows-Benutzerprofil konnte nicht bestimmt werden.");
        }
        return Environment.ExpandEnvironmentVariables(profile);
    }

    public static string LocalAppData() => Path.Combine(ProfilePath(), "AppData", "Local");

    public static string Desktop() => Path.Combine(ProfilePath(), "Desktop");
}
