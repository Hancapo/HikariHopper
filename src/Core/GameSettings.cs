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
    public const string OpenLastGameOnStartupKey = "openLastGameOnStartup";
    public const string LegacyEdition = "legacy";
    public const string EnhancedEdition = "enhanced";

    private const string LegacyName = "Hikari Hopper";
    private const int SettingsSchemaVersion = 3;

    public static IEnumerable<string> AllEditions => GameCatalog.Editions.Select(edition => edition.Id);

    private static GameEditionInfo Info(string edition) =>
        GameCatalog.Edition(edition) ?? throw new ArgumentException($"No such edition: {edition}", nameof(edition));

    public static string GameRootKey(string edition) => Info(edition).SettingsKey;

    public static string GameExecutable(string edition) => Info(edition).Executable;

    public static string BrowseTitle(string edition) => Info(edition).BrowseTitle;

    /// <summary>
    /// The edition installed at <paramref name="value"/>, by the executable beside it, in the catalog's order (GTA V
    /// Enhanced before Legacy); "" when it isn't a game's folder.
    /// </summary>
    public static string GameEditionForPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || !Directory.Exists(value))
            return "";
        foreach (GameEditionInfo edition in GameCatalog.Editions)
            if (File.Exists(Path.Combine(value, edition.Executable)))
                return edition.Id;
        return "";
    }

    public static bool IsGameRoot(string? value, string edition = "")
    {
        string detected = GameEditionForPath(value);
        return edition.Length > 0 ? detected == edition : detected.Length > 0;
    }

    public static string ConfiguredGameRoot(string edition) => AppSettings.GetString(GameRootKey(edition));

    /// <summary>Whether the app reopens the last game when it starts (on unless turned off in Settings).</summary>
    public static bool OpenLastGameOnStartup
    {
        get => AppSettings.GetInt(OpenLastGameOnStartupKey, 1) != 0;
        set => AppSettings.SetInt(OpenLastGameOnStartupKey, value ? 1 : 0);
    }

    /// <summary>The game to reopen at startup: the configured one, unless that is turned off.</summary>
    public static string GameRootToRestore() => OpenLastGameOnStartup ? ConfiguredGameRootToOpen() : "";

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
        foreach (string candidate in AllEditions)
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
