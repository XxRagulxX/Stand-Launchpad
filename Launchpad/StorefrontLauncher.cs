using System.Diagnostics;
using Microsoft.Win32;

namespace Launchpad;

internal static class StorefrontLauncher
{
    public static bool TryLaunch(LauncherId launcher, out string? error)
    {
        switch (launcher)
        {
            case LauncherId.Steam:
                return TryOpenUrl("steam://run/271590", out error);
            case LauncherId.EpicGames:
                return TryOpenUrl(
                    "com.epicgames.launcher://apps/9d2d0eb64d5c44529cece33fe2a46482?action=launch&silent=true",
                    out error);
            case LauncherId.RockstarGames:
                return TryLaunchRockstar(out error);
            default:
                error = $"Unknown launcher '{launcher}'.";
                return false;
        }
    }

    private static bool TryOpenUrl(string url, out string? error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryLaunchRockstar(out string? error)
    {
        try
        {
            string? installPath = null;
            string[] regKeys =
            {
                @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V Enhanced",
                @"SOFTWARE\Rockstar Games\Grand Theft Auto V Enhanced",
                @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V",
                @"SOFTWARE\Rockstar Games\Grand Theft Auto V",
            };
            foreach (var regKey in regKeys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regKey);
                if (key?.GetValue("InstallFolder") is string p) { installPath = p; break; }
            }

            if (installPath != null)
            {
                foreach (var exe in new[] { "PlayGTAV.exe", "GTA5_Enhanced.exe" })
                {
                    var full = Path.Combine(installPath, exe);
                    if (!File.Exists(full)) continue;
                    Process.Start(full);
                    error = null;
                    return true;
                }
            }

            // Registry missing or exe not there — scan common install locations.
            foreach (var root in new[] { @"C:\Program Files\Rockstar Games",
                                         @"C:\Program Files (x86)\Rockstar Games" })
            {
                foreach (var dir in new[] { "Grand Theft Auto V Enhanced", "Grand Theft Auto V" })
                {
                    foreach (var exe in new[] { "PlayGTAV.exe", "GTA5_Enhanced.exe" })
                    {
                        var full = Path.Combine(root, dir, exe);
                        if (!File.Exists(full)) continue;
                        Process.Start(full);
                        error = null;
                        return true;
                    }
                }
            }

            error = "Couldn't find GTA V Enhanced. Start it from the Rockstar Games Launcher — " +
                    "Launchpad will inject automatically when GTA5_Enhanced.exe starts.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch via Rockstar Games: {ex.Message}";
            return false;
        }
    }
}
