using System.Runtime.Versioning;

namespace HikariHopper.Core;

/// <summary>Game-installation preferences and the settings schema migration.</summary>
[SupportedOSPlatform("windows")]
public static class GameSettings
{
    public const string LastGameRootKey = "gameRoot";
    public const string LegacyGameRootKey = "gameRoots/legacy";
    public const string EnhancedGameRootKey = "gameRoots/enhanced";
    public const string RecentGameRootsKey = "recentGameRoots";
    public const string LegacyEdition = "legacy";
    public const string EnhancedEdition = "enhanced";

    private const string LegacyName = "Hikari Hopper";
    private const int SettingsSchemaVersion = 3;

    private static readonly Dictionary<string, (string Key, string Executable, string Title)> Editions = new()
    {
        [LegacyEdition] = (LegacyGameRootKey, "GTA5.exe", "Select GTA V Legacy Installation"),
        [EnhancedEdition] = (EnhancedGameRootKey, "GTA5_Enhanced.exe", "Select GTA V Enhanced Installation"),
    };

    public static IEnumerable<string> AllEditions => Editions.Keys;

    public static string GameRootKey(string edition) => Editions[edition].Key;

    public static string GameExecutable(string edition) => Editions[edition].Executable;

    public static string BrowseTitle(string edition) => Editions[edition].Title;

    /// <summary>The GTA V edition installed at <paramref name="value"/>, preferring Enhanced.</summary>
    public static string GameEditionForPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || !Directory.Exists(value))
            return "";
        if (File.Exists(Path.Combine(value, GameExecutable(EnhancedEdition))))
            return EnhancedEdition;
        if (File.Exists(Path.Combine(value, GameExecutable(LegacyEdition))))
            return LegacyEdition;
        return "";
    }

    public static bool IsGameRoot(string? value, string edition = "")
    {
        string detected = GameEditionForPath(value);
        return edition.Length > 0 ? detected == edition : detected.Length > 0;
    }

    public static string ConfiguredGameRoot(string edition) => AppSettings.GetString(GameRootKey(edition));

    public static string ConfiguredGameRootToOpen(string edition = "")
    {
        if (edition.Length > 0)
        {
            string path = ConfiguredGameRoot(edition);
            return IsGameRoot(path, edition) ? path : "";
        }
        string lastRoot = AppSettings.GetString(LastGameRootKey);
        if (IsGameRoot(lastRoot))
            return lastRoot;
        foreach (string candidate in new[] { EnhancedEdition, LegacyEdition })
        {
            string path = ConfiguredGameRoot(candidate);
            if (IsGameRoot(path, candidate))
                return path;
        }
        return "";
    }

    /// <summary>Remembers the last opened game and its edition-specific installation.</summary>
    public static void RememberGameRoot(string value)
    {
        string edition = GameEditionForPath(value);
        if (edition.Length == 0)
            return;
        AppSettings.SetString(LastGameRootKey, value);
        AppSettings.SetString(GameRootKey(edition), value);
    }

    public static void MigrateLegacySettings()
    {
        int schemaVersion = AppSettings.GetInt("settingsSchemaVersion");
        if (schemaVersion >= SettingsSchemaVersion)
            return;

        if (schemaVersion < 1)
        {
            string legacyPath = $@"Software\{LegacyName}\{LegacyName}";
            string legacyRoot = Convert.ToString(AppSettings.ReadFrom(legacyPath, LastGameRootKey)) ?? "";
            if (GameEditionForPath(legacyRoot).Length > 0)
                AppSettings.SetString(LastGameRootKey, legacyRoot);
            var recent = RecentRoots(AppSettings.ToList(AppSettings.ReadFrom(legacyPath, RecentGameRootsKey)));
            if (recent.Count == 0)
                recent = RecentRoots(AppSettings.GetStringList(RecentGameRootsKey));
            AppSettings.SetStringList(RecentGameRootsKey, recent);
        }

        if (schemaVersion < 2)
        {
            string lastRoot = AppSettings.GetString(LastGameRootKey);
            string edition = GameEditionForPath(lastRoot);
            if (edition.Length > 0)
                AppSettings.SetString(GameRootKey(edition), lastRoot);
        }

        if (schemaVersion < 3)
        {
            string lastRoot = AppSettings.GetString(LastGameRootKey);
            if (GameEditionForPath(lastRoot).Length == 0)
            {
                foreach (string edition in new[] { EnhancedEdition, LegacyEdition })
                {
                    string configured = ConfiguredGameRoot(edition);
                    if (IsGameRoot(configured, edition))
                    {
                        AppSettings.SetString(LastGameRootKey, configured);
                        break;
                    }
                }
            }
        }

        AppSettings.SetInt("settingsSchemaVersion", SettingsSchemaVersion);
    }

    private static List<string> RecentRoots(IReadOnlyList<string> candidates)
    {
        var roots = new List<string>();
        foreach (string path in candidates)
            if (IsGameRoot(path) && !roots.Contains(path))
                roots.Add(path);
        return roots;
    }
}
