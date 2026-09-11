using System.Diagnostics;
using Microsoft.Win32;

namespace Launchpad.Injector;

/// <summary>
/// Launches Epic Games/Rockstar Games from *inside* the Windows/Wine process
/// this executable runs in - the same registry lookups and protocol URLs
/// that work natively on Windows also work here when run under `wine`
/// against the Proton prefix a user has installed the Epic Games Launcher
/// or Rockstar Games Launcher into (the common setup for the Epic/Rockstar
/// editions of GTA V on Linux, since GTA5 needs its parent launcher present
/// in the same prefix to run at all).
/// </summary>
internal static class StorefrontLauncher
{
    public static bool TryLaunch(string target, out string? error)
    {
        switch (target)
        {
            case "epic":
                return TryLaunchEpic(out error);
            case "rockstar":
                return TryLaunchRockstar(out error);
            default:
                error = $"Unknown launch target '{target}'.";
                return false;
        }
    }

    private static bool TryLaunchEpic(out string? error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "com.epicgames.launcher://apps/9d2d0eb64d5c44529cece33fe2a46482?action=launch&silent=true")
            { UseShellExecute = true });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Couldn't launch via Epic Games - is it installed in this prefix? ({ex.Message})";
            return false;
        }
    }

    private static bool TryLaunchRockstar(out string? error)
    {
        try
        {
            // Try registry keys: original GTA V, Enhanced edition, and 64-bit hive variants.
            string? installPath = null;
            string[] regKeys =
            {
                @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V",
                @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V Enhanced",
                @"SOFTWARE\Rockstar Games\Grand Theft Auto V",
                @"SOFTWARE\Rockstar Games\Grand Theft Auto V Enhanced",
            };
            foreach (var regKey in regKeys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regKey);
                if (key?.GetValue("InstallFolder") is string p) { installPath = p; break; }
            }

            if (installPath != null)
            {
                // Try the play launcher executable; Enhanced uses the same name.
                foreach (var exe in new[] { "PlayGTAV.exe", "GTAVLauncher.exe" })
                {
                    var full = Path.Combine(installPath, exe);
                    if (File.Exists(full))
                    {
                        Process.Start(full);
                        error = null;
                        return true;
                    }
                }
            }

            // Registry not found or exe missing: scan common Rockstar install paths.
            string[] searchRoots = {
                @"C:\Program Files\Rockstar Games",
                @"C:\Program Files (x86)\Rockstar Games",
            };
            string[] gameDirs = {
                "Grand Theft Auto V Enhanced",
                "Grand Theft Auto V",
            };
            string[] exeNames = { "PlayGTAV.exe", "GTAVLauncher.exe" };
            foreach (var root in searchRoots)
            {
                foreach (var gameDir in gameDirs)
                {
                    foreach (var exeName in exeNames)
                    {
                        var full = Path.Combine(root, gameDir, exeName);
                        if (File.Exists(full))
                        {
                            Process.Start(full);
                            error = null;
                            return true;
                        }
                    }
                }
            }

            error = "Couldn't find GTA V in the registry or at common install paths. " +
                    "Start the game manually from the Rockstar Games Launcher — " +
                    "Launchpad will inject when it detects GTA5.exe.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch via Rockstar Games: {ex.Message}";
            return false;
        }
    }
}
