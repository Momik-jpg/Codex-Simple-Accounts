using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAccountTray;

public sealed record AppSettings(string ProjectFolder, bool AutoSwitchEnabled = false)
{
    public string RouterProfile { get; init; } = "auto";
    public string RouterEffort { get; init; } = "auto";
    public int RouterMaxSubagents { get; init; } = 2;

    [JsonIgnore]
    public TaskRouterPreferences RouterPreferences =>
        new TaskRouterPreferences(RouterProfile, RouterEffort, RouterMaxSubagents).Normalize();
}

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
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile))
                ?? CreateDefaultSettings();
            TaskRouterPreferences router = settings.RouterPreferences;
            return settings with
            {
                RouterProfile = router.Profile,
                RouterEffort = router.Effort,
                RouterMaxSubagents = router.MaxSubagents
            };
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
