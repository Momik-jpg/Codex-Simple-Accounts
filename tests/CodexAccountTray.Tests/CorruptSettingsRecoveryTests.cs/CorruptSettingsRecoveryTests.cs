using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class CorruptSettingsRecoveryTests
{
    [Fact]
    public void Load_CorruptSettings_PreservesBackupAndReturnsDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexSettings-{Guid.NewGuid():N}");
        string settingsPath = Path.Combine(root, "settings.json");
        const string corruptContent = "{ invalid json";

        Directory.CreateDirectory(root);
        File.WriteAllText(settingsPath, corruptContent);

        try
        {
            var settings = new AppSettingsStore(new AppPaths(root)).Load();

            Assert.False(settings.AutoSwitchEnabled);

            string[] backups = Directory.GetFiles(root, "settings.json.corrupt-*.bak");
            string backupPath = Assert.Single(backups);

            Assert.Equal(corruptContent, File.ReadAllText(backupPath));
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
