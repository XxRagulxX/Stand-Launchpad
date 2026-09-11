using System.Text.Json;

namespace Launchpad;

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

/// <summary>
/// Plain JSON settings, replacing the old System.Configuration
/// (Properties.Settings) store, which only worked on .NET Framework/Windows.
/// Lives under the standard cross-platform ApplicationData folder, which
/// .NET resolves correctly on both native Linux and under Wine.
/// </summary>
internal sealed class LaunchpadSettings
{
    public bool AutoInject { get; set; }
    public int AutoInjectDelaySeconds { get; set; }
    public LauncherId GameLauncher { get; set; } = LauncherId.Steam;
    public List<DllEntry> Dlls { get; set; } = new();
    public string? ProtonPrefixOverride { get; set; }
    /// <summary>
    /// Full path to the wine/wine64 binary to use on Linux. When empty,
    /// InjectorRunner auto-detects Heroic Games Launcher's Proton-GE, then
    /// falls back to the system `wine`.
    /// </summary>
    public string? WinePathOverride { get; set; }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launchpad", "settings.json");

    public static LaunchpadSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<LaunchpadSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt/unreadable settings - fall back to defaults rather than crash.
        }
        return new LaunchpadSettings();
    }

    public void Save()
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
