using System.Diagnostics;

namespace Launchpad;

internal sealed record LauncherOption(LauncherId Id, string Name);

internal static class GameLauncher
{
    /// <summary>
    /// Launches GTA V through the chosen storefront. On Linux, launchers are
    /// driven entirely through registered URL protocol handlers (xdg-open),
    /// since there's no registry to probe and Rockstar's own launcher has no
    /// native Linux client at all.
    /// </summary>
    public static void Launch(LauncherId launcher, Action<string> showError)
    {
        switch (launcher)
        {
            case LauncherId.Steam:
                OpenUrl("steam://run/271590");
                break;

            case LauncherId.EpicGames:
                if (!OperatingSystem.IsWindows())
                {
                    showError("The Epic Games launcher isn't available on Linux.");
                    break;
                }
                OpenUrl("com.epicgames.launcher://apps/9d2d0eb64d5c44529cece33fe2a46482?action=launch&silent=true");
                break;

            case LauncherId.RockstarGames:
                if (!OperatingSystem.IsWindows())
                {
                    showError("The Rockstar Games Launcher isn't available on Linux.");
                    break;
                }
                LaunchRockstarWindows(showError);
                break;
        }
    }

    /// <summary>Storefronts available for the current OS, in display order.</summary>
    public static IReadOnlyList<LauncherOption> AvailableLaunchers =>
        OperatingSystem.IsWindows()
            ? new[]
            {
                new LauncherOption(LauncherId.Steam, "Steam"),
                new LauncherOption(LauncherId.EpicGames, "Epic Games"),
                new LauncherOption(LauncherId.RockstarGames, "Rockstar Games"),
            }
            : new[] { new LauncherOption(LauncherId.Steam, "Steam") };

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void LaunchRockstarWindows(Action<string> showError)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V");
            var path = key?.GetValue("InstallFolder") as string;
            if (path != null)
            {
                Process.Start(Path.Combine(path, "PlayGTAV.exe"));
            }
            else
            {
                showError("Couldn't find the Rockstar Games installation.");
            }
        }
        catch (Exception ex)
        {
            showError($"Failed to launch via Rockstar Games: {ex.Message}");
        }
    }
}
