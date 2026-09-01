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
            return new AppSettings(WindowsUserProfile.Desktop());
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile))
            ?? new AppSettings(WindowsUserProfile.Desktop());
    }

    public void Save(AppSettings settings)
    {
        _paths.EnsureCreated();
        string temporary = _settingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, _settingsFile, true);
    }
}
