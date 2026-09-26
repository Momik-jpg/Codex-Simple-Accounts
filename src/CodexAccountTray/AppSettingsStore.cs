using System.Text.Json;

namespace CodexAccountTray;

public sealed record AppSettings(string ProjectFolder, bool AutoSwitchEnabled = false);

public sealed class AppSettingsStore
{
    private readonly AppPaths _paths;
    private readonly string _settingsFile;

    public AppSettingsStore(AppPaths paths)
    {
        _paths = paths;
        _settingsFile = Path.Combine(paths.Root, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFile))
        {
            return CreateDefaultSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile))
                ?? CreateDefaultSettings();
        }
        catch (JsonException)
        {
            PreserveCorruptSettingsFile();
            return CreateDefaultSettings();
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings(WindowsUserProfile.Desktop());
    }

    private void PreserveCorruptSettingsFile()
    {
        string backupFile = $"{_settingsFile}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
        File.Move(_settingsFile, backupFile);
    }

    public void Save(AppSettings settings)
    {
        _paths.EnsureCreated();
        string temporary = _settingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, _settingsFile, true);
    }
}
