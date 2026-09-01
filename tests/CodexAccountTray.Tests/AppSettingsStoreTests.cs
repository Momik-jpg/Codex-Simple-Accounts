using CodexAccountTray;

namespace CodexAccountTray.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RestoresProjectFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new AppSettingsStore(new AppPaths(root));
            store.Save(new AppSettings("C:\\Projekt", true));

            Assert.Equal("C:\\Projekt", store.Load().ProjectFolder);
            Assert.True(store.Load().AutoSwitchEnabled);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_OldSettings_DefaultsAutoSwitchToFalse()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"ProjectFolder\":\"C:\\\\Projekt\"}");

            var settings = new AppSettingsStore(new AppPaths(root)).Load();

            Assert.False(settings.AutoSwitchEnabled);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
