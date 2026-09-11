using System.Diagnostics;

namespace Launchpad;

internal sealed record LauncherOption(LauncherId Id, string Name);

internal static class GameLauncher
{
    /// <summary>All three storefronts are offered on every OS. On Linux, Epic
    /// Games/Rockstar Games are handled by the caller through
    /// <see cref="InjectorRunner.LaunchAsync"/> instead of this method - they
    /// run inside the GTA V Proton prefix via Wine (the usual setup for the
    /// Epic/Rockstar editions of GTA V on Linux), since that's where their
    /// own protocol handler/registry entries actually live.</summary>
    public static IReadOnlyList<LauncherOption> AvailableLaunchers { get; } = new[]
    {
        new LauncherOption(LauncherId.Steam, "Steam"),
        new LauncherOption(LauncherId.EpicGames, "Epic Games"),
        new LauncherOption(LauncherId.RockstarGames, "Rockstar Games"),
    };

    /// <summary>
    /// Launches GTA V through the chosen storefront using this process's own
    /// context directly - i.e. natively on Windows, or (for Steam only) via
    /// the native Linux steam:// protocol handler, which doesn't need Wine.
    /// </summary>
    public static void Launch(LauncherId launcher, Action<string> showError)
    {
        switch (launcher)
        {
            case LauncherId.Steam:
                OpenUrl("steam://run/271590");
                break;

            case LauncherId.EpicGames:
                OpenUrl("com.epicgames.launcher://apps/9d2d0eb64d5c44529cece33fe2a46482?action=launch&silent=true");
                break;

            case LauncherId.RockstarGames:
                if (!OperatingSystem.IsWindows())
                {
                    showError("Rockstar Games launch requires the Wine/Proton flow on Linux.");
                    break;
                }
                LaunchRockstarWindows(showError);
                break;
        }
    }

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
