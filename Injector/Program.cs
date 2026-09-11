namespace Launchpad.Injector;

/// <summary>
/// Launchpad.Injector - the only Windows-only, P/Invoke-using binary in the
/// solution. The UI (Launchpad, cross-platform Avalonia) never P/Invokes
/// directly; it runs this executable instead - directly on Windows, or via
/// `wine` against the game's Proton prefix on Linux.
///
/// Usage:
///   Launchpad.Injector watch
///     Long-running. Polls for a GTA5 process once a second and prints
///     "STARTED &lt;pid&gt;" / "STOPPED" lines to stdout as the state changes.
///     Runs until stdin is closed or the process is killed.
///
///   Launchpad.Injector inject &lt;pid&gt; &lt;dll path&gt; [dll path...]
///     One-shot. Injects each DLL into the given pid, printing progress
///     lines and a final "INJECTED &lt;count&gt;/&lt;total&gt;" line, then exits
///     with that count as its exit code.
///
///   Launchpad.Injector launch &lt;epic|rockstar&gt;
///     One-shot. Launches the given storefront from inside this process's
///     Windows/Wine context (registry lookups + protocol URLs), printing
///     "LAUNCHED" or "ERROR &lt;message&gt;", exit code 0/1. Steam isn't handled
///     here - its steam:// protocol already works from the native Linux
///     side without Wine, so the UI calls it directly.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: Launchpad.Injector <watch|inject|launch> [args...]");
            return -1;
        }

        return args[0] switch
        {
            "watch" => RunWatch(),
            "inject" => RunInject(args[1..]),
            "launch" => RunLaunch(args[1..]),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine($"Unknown mode '{mode}'.");
        return -1;
    }

    private static int RunWatch()
    {
        int lastPid = 0;
        // Runs until the parent kills this process (it owns our lifetime).
        while (true)
        {
            int pid = GameProcess.FindPid();
            if (pid != lastPid)
            {
                Console.WriteLine(pid != 0 ? $"STARTED {pid}" : "STOPPED");
                Console.Out.Flush();
                lastPid = pid;
            }
            Thread.Sleep(1000);
        }
    }

    private static int RunInject(string[] rest)
    {
        if (rest.Length < 2 || !int.TryParse(rest[0], out int pid))
        {
            Console.Error.WriteLine("Usage: Launchpad.Injector inject <pid> <dll path> [dll path...]");
            return -1;
        }

        var dlls = rest[1..];
        string tempDir = Path.Combine(Path.GetTempPath(), "LaunchpadInjector");
        int injected = DllInjector.InjectAll(pid, dlls, tempDir, line => Console.WriteLine(line));

        Console.WriteLine($"INJECTED {injected}/{dlls.Length}");
        return injected;
    }

    private static int RunLaunch(string[] rest)
    {
        if (rest.Length < 1)
        {
            Console.Error.WriteLine("Usage: Launchpad.Injector launch <epic|rockstar>");
            return -1;
        }

        bool ok = StorefrontLauncher.TryLaunch(rest[0], out var error);
        Console.WriteLine(ok ? "LAUNCHED" : $"ERROR {error}");
        return ok ? 0 : 1;
    }
}
