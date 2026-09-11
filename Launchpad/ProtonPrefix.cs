namespace Launchpad;

/// <summary>
/// Locates the Wine/Proton prefix Steam created for GTA V, so the Injector
/// can be run inside the exact same WINEPREFIX the game process itself lives
/// in - a generic system Wine won't see the game's process table.
///
/// Searched App IDs (first match wins):
///   271590  – GTA V (original Steam release)
///   2060170 – GTA V Enhanced (standalone Steam entry)
///   1404890 – Rockstar Games Launcher on Steam (hosts GTA V Enhanced when
///              launched through the Rockstar store or Rockstar Launcher)
/// </summary>
internal static class ProtonPrefix
{
    // Priority order: most-specific first so we prefer the actual game prefix
    // over the launcher prefix when both exist.
    private static readonly string[] AppIds = { "271590", "2060170", "1404890" };

    private static readonly string[] SteamRoots =
    {
        "~/.steam/steam",
        "~/.local/share/Steam",
        "~/.var/app/com.valvesoftware.Steam/.local/share/Steam", // Flatpak Steam
    };

    /// <summary>
    /// Returns the WINEPREFIX path (the "pfx" folder) for GTA V, or null if
    /// it can't be found. Checks the user override in settings first.
    /// </summary>
    public static string? Find(LaunchpadSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ProtonPrefixOverride) && Directory.Exists(settings.ProtonPrefixOverride))
        {
            return settings.ProtonPrefixOverride;
        }

        foreach (var appId in AppIds)
        {
            foreach (var root in SteamRoots)
            {
                var candidate = Path.Combine(Expand(root), "steamapps", "compatdata", appId, "pfx");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Steam can also point library folders elsewhere; check libraryfolders.vdf.
            foreach (var root in SteamRoots)
            {
                var vdf = Path.Combine(Expand(root), "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                {
                    continue;
                }

                foreach (var libraryPath in ParseLibraryPaths(vdf))
                {
                    var candidate = Path.Combine(libraryPath, "steamapps", "compatdata", appId, "pfx");
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ParseLibraryPaths(string vdfPath)
    {
        foreach (var line in File.ReadLines(vdfPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("\"path\""))
            {
                continue;
            }

            var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
            // Tokens: path, <the value>
            if (parts.Length >= 2)
            {
                yield return parts[^1].Replace("\\\\", "/");
            }
        }
    }

    private static string Expand(string path) =>
        path.StartsWith("~")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
}
