using System.Diagnostics;

namespace Launchpad;

/// <summary>
/// Runs Launchpad.Injector.exe as a child process - directly on Windows, or
/// via `wine` against the game's Proton prefix on Linux. This is the only
/// place the UI touches the injector; it never P/Invokes anything itself.
/// </summary>
internal sealed class InjectorRunner
{
    public event Action<int>? GameStarted;
    public event Action? GameStopped;

    private readonly string _injectorPath;
    private readonly LaunchpadSettings _settings;
    private Process? _watchProcess;

    public InjectorRunner(LaunchpadSettings settings)
    {
        _settings = settings;
        _injectorPath = Path.Combine(AppContext.BaseDirectory, "Injector", "Launchpad.Injector.exe");
    }

    public string? UnavailableReason { get; private set; }

    /// <summary>Starts the background "is the game running" watcher.</summary>
    public bool StartWatching()
    {
        if (!TryBuildStartInfo("watch", Array.Empty<string>(), out var startInfo))
        {
            return false;
        }

        _watchProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _watchProcess.OutputDataReceived += (_, e) => OnWatchLine(e.Data);
        _watchProcess.Start();
        _watchProcess.BeginOutputReadLine();
        return true;
    }

    private void OnWatchLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        if (line.StartsWith("STARTED "))
        {
            if (int.TryParse(line.AsSpan(8), out int pid))
            {
                GameStarted?.Invoke(pid);
            }
        }
        else if (line == "STOPPED")
        {
            GameStopped?.Invoke();
        }
    }

    /// <summary>Injects the given DLLs into the running game. Returns the count injected.</summary>
    public async Task<int> InjectAsync(int pid, IReadOnlyList<string> dllPaths, Action<string> log)
    {
        var args = new List<string> { "inject", pid.ToString() };
        args.AddRange(dllPaths);

        if (!TryBuildStartInfo(args[0], args.Skip(1), out var startInfo))
        {
            log(UnavailableReason ?? "Injector is unavailable.");
            return 0;
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    public void StopWatching()
    {
        try
        {
            if (_watchProcess is { HasExited: false })
            {
                _watchProcess.Kill();
            }
        }
        catch
        {
            // best effort
        }
    }

    private bool TryBuildStartInfo(string mode, IEnumerable<string> extraArgs, out ProcessStartInfo startInfo)
    {
        UnavailableReason = null;

        if (!File.Exists(_injectorPath))
        {
            UnavailableReason = $"Injector executable not found at '{_injectorPath}'.";
            startInfo = null!;
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo(_injectorPath)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }
        else
        {
            var prefix = ProtonPrefix.Find(_settings);
            if (prefix == null)
            {
                UnavailableReason = "Couldn't find GTA V's Proton prefix. Launch the game at least once via Steam, " +
                                     "or set a custom prefix path in settings.";
                startInfo = null!;
                return false;
            }

            startInfo = new ProcessStartInfo("wine")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(_injectorPath);
            startInfo.EnvironmentVariables["WINEPREFIX"] = prefix;
        }

        startInfo.ArgumentList.Add(mode);
        foreach (var arg in extraArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return true;
    }
}
