using System.Text.Json;

namespace IsConnected;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string settingsPath;

    public AppSettingsStore()
        : this(GetDefaultSettingsPath())
    {
    }

    public AppSettingsStore(string settingsPath)
    {
        this.settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
            if (settings is null)
            {
                return new AppSettings();
            }

            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
    }

    private static string GetDefaultSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IsConnected",
            "settings.json");
}
