using System.Diagnostics;

namespace Launchpad;

internal static class GameProcess
{
    private const string ProcessName = "GTA5_Enhanced";

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
