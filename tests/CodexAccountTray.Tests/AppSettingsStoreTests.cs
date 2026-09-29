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
            Assert.Equal("auto", store.Load().RouterProfile);
            Assert.Equal("auto", store.Load().RouterModel);
            Assert.Equal(2, store.Load().RouterMaxSubagents);
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
            Assert.Equal("auto", settings.RouterProfile);
            Assert.Equal("auto", settings.RouterModel);
            Assert.Equal("auto", settings.RouterEffort);
            Assert.Equal(2, settings.RouterMaxSubagents);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_NormalizesInvalidRouterPreferences()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"),
                "{\"ProjectFolder\":\"C:\\\\Projekt\",\"RouterProfile\":\"turbo\",\"RouterModel\":\"invalid model; command\",\"RouterEffort\":\"huge\",\"RouterMaxSubagents\":9}");

            AppSettings settings = new AppSettingsStore(new AppPaths(root)).Load();

            Assert.Equal("auto", settings.RouterProfile);
            Assert.Equal("auto", settings.RouterModel);
            Assert.Equal("auto", settings.RouterEffort);
            Assert.Equal(2, settings.RouterMaxSubagents);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveAndLoad_RestoresRouterPreferences()
    {
        string root = Path.Combine(Path.GetTempPath(), $"CodexSettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new AppSettingsStore(new AppPaths(root));
            store.Save(new AppSettings("C:\\Projekt")
            {
                RouterProfile = "deep",
                RouterModel = "gpt-5.5",
                RouterEffort = "xhigh",
                RouterMaxSubagents = 1
            });

            TaskRouterPreferences preferences = store.Load().RouterPreferences;

            Assert.Equal("deep", preferences.Profile);
            Assert.Equal("gpt-5.5", preferences.Model);
            Assert.Equal("xhigh", preferences.Effort);
            Assert.Equal(1, preferences.MaxSubagents);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
