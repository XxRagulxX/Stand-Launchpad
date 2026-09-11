namespace Launchpad;

/// <summary>
/// Bridges the UI to the injection and game-watch logic.
/// Everything runs in-process — no child exe, no stdout parsing.
/// </summary>
internal sealed class InjectorRunner
{
    public event Action<int>? GameStarted;
    public event Action? GameStopped;

    private CancellationTokenSource? _watchCts;

    public void StartWatching()
    {
        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;

        Task.Run(async () =>
        {
            int lastPid = 0;
            while (!token.IsCancellationRequested)
            {
                int pid = GameProcess.FindPid();
                if (pid != lastPid)
                {
                    if (pid != 0) GameStarted?.Invoke(pid);
                    else          GameStopped?.Invoke();
                    lastPid = pid;
                }
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    public void StopWatching()
    {
        _watchCts?.Cancel();
        _watchCts = null;
    }

    public async Task<int> InjectAsync(int pid, IReadOnlyList<string> dllPaths, Action<string> log)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LaunchpadInjector");
        return await Task.Run(() => DllInjector.InjectAll(pid, dllPaths, tempDir, log));
    }

    public async Task<string?> LaunchAsync(LauncherId launcher)
    {
        return await Task.Run(() =>
        {
            StorefrontLauncher.TryLaunch(launcher, out var error);
            return error;
        });
    }
}
