using System.Diagnostics;

namespace Launchpad.Injector;

internal static class GameProcess
{
    private const string ProcessName = "GTA5";

    /// <summary>
    /// Finds the running GTA5 process id, or 0 if it isn't running.
    ///
    /// Must run from inside the same Windows/Wine process table as the game:
    /// on Linux the caller launches this whole executable under `wine`
    /// pointed at the Proton prefix, so Process.GetProcessesByName here sees
    /// Wine's Windows-PID view, not the Linux host's PID view.
    /// </summary>
    public static int FindPid()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                return process.Id;
            }
        }
        return 0;
    }
}
