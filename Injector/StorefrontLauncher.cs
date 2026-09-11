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
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V");
            var path = key?.GetValue("InstallFolder") as string;
            if (path == null)
            {
                error = "Couldn't find the Rockstar Games installation in this prefix.";
                return false;
            }
            Process.Start(Path.Combine(path, "PlayGTAV.exe"));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch via Rockstar Games: {ex.Message}";
            return false;
        }
    }
}
