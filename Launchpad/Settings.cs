using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launchpad;

[JsonSerializable(typeof(LaunchpadSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext { }

internal enum LauncherId
{
    Steam = 0,
    EpicGames = 1,
    RockstarGames = 2,
}

internal sealed class DllEntry
{
    public string Path { get; set; } = "";
    public bool Checked { get; set; }
}

internal sealed class LaunchpadSettings
{
    public bool AutoInject { get; set; }
    public int AutoInjectDelaySeconds { get; set; }
    public LauncherId GameLauncher { get; set; } = LauncherId.Steam;
    public List<DllEntry> Dlls { get; set; } = new();
    public bool Advanced { get; set; }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Launchpad", "settings.json");

    public static LaunchpadSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), AppJsonContext.Default.LaunchpadSettings);
                if (loaded != null) return loaded;
            }
        }
        catch { }
        return new LaunchpadSettings();
    }

    public void Save()
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, AppJsonContext.Default.LaunchpadSettings));
    }
}
