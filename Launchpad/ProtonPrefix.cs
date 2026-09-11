namespace Launchpad;

/// <summary>
/// Locates the Wine/Proton prefix Steam created for GTA V (app id 271590),
/// so the Injector can be run inside the exact same WINEPREFIX the game
/// process itself lives in - a generic system Wine won't see the game's
/// process table.
/// </summary>
internal static class ProtonPrefix
{
    private const string GtaVAppId = "271590";

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

        foreach (var root in SteamRoots)
        {
            var expanded = Expand(root);
            var candidate = Path.Combine(expanded, "steamapps", "compatdata", GtaVAppId, "pfx");
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
                var candidate = Path.Combine(libraryPath, "steamapps", "compatdata", GtaVAppId, "pfx");
                if (Directory.Exists(candidate))
                {
                    return candidate;
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
