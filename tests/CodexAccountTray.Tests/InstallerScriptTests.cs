namespace CodexAccountTray.Tests;

public sealed class InstallerScriptTests
{
    [Fact]
    public void PostInstallActionStartsInstalledAppDirectly()
    {
        var scriptPath = FindRepositoryFile("installer", "CodexAccountTray.iss");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("Filename: \"{app}\\{#AppExeName}\"", script);
        Assert.DoesNotContain("explorer.exe", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {Path.Combine(parts)}");
    }
}
