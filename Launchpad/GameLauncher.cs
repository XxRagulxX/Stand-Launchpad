namespace Launchpad;

internal sealed record LauncherOption(LauncherId Id, string Name);

internal static class GameLauncher
{
    public static IReadOnlyList<LauncherOption> AvailableLaunchers { get; } = new[]
    {
        new LauncherOption(LauncherId.Steam,         "Steam"),
        new LauncherOption(LauncherId.EpicGames,     "Epic Games"),
        new LauncherOption(LauncherId.RockstarGames, "Rockstar Games"),
    };
}
