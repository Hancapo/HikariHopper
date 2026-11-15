using System.Runtime.Versioning;

namespace HikariHopper.Core;

/// <summary>
/// One edition of a supported game: its id, unique across games and keyed like the settings
/// (<see cref="GameSettings"/>); its name within the game ("Legacy"); the executable that marks an installation of it;
/// and where its installation's folder is kept.
/// </summary>
public sealed record GameEditionInfo(string Id, string Title, string Executable, string SettingsKey)
{
    /// <summary>The game it belongs to; set by <see cref="GameCatalog"/>.</summary>
    public SupportedGame Game { get; internal set; } = null!;

    /// <summary>Its short name with the game's, as the status bar and tabs show it: "GTA V Legacy".</summary>
    public string ShortName => $"{Game.ShortTitle} {Title}";

    /// <summary>Its full name, as the loading screen says it: "Grand Theft Auto V Legacy".</summary>
    public string FullName => $"{Game.Title} {Title}";

    /// <summary>The folder picker's title for its installation.</summary>
    public string BrowseTitle => $"Select {ShortName} Installation";
}

/// <summary>A game HikariHopper can open, with its editions in the order the launcher lists them.</summary>
public sealed record SupportedGame(string Id, string Title, string ShortTitle, IReadOnlyList<GameEditionInfo> Editions);

/// <summary>One installation the start page offers to open.</summary>
/// <param name="Game">The game it belongs to.</param>
/// <param name="Edition">Its edition id, or "" for a game that has no installation set up.</param>
/// <param name="Title">The edition's name, or the folder name of a recent installation.</param>
/// <param name="Path">The installation folder, "" when not set up.</param>
public sealed record LauncherEntry(SupportedGame Game, string Edition, string Title, string Path)
{
    public bool SetUp => Path.Length > 0;
}

/// <summary>
/// The games HikariHopper can open. The start page lists them as groups, so another game (RDR2, …)
/// is one more entry here plus its mark, not a new layout.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GameCatalog
{
    public const string GtaV = "gta5";

    // Each game's editions, in the order an installation is recognised: a folder with both executables is the first.
    public static IReadOnlyList<SupportedGame> Games { get; } = Link(
    [
        new(GtaV, "Grand Theft Auto V", "GTA V",
        [
            new(GameSettings.EnhancedEdition, "Enhanced", "GTA5_Enhanced.exe", GameSettings.EnhancedGameRootKey),
            new(GameSettings.LegacyEdition, "Legacy", "GTA5.exe", GameSettings.LegacyGameRootKey),
        ]),
    ]);

    /// <summary>Every edition of every game, in the order an installation is recognised.</summary>
    public static IReadOnlyList<GameEditionInfo> Editions { get; } = [.. Games.SelectMany(game => game.Editions)];

    /// <summary>The edition with id <paramref name="id"/>, or null.</summary>
    public static GameEditionInfo? Edition(string? id) => Editions.FirstOrDefault(edition => edition.Id == id);

    private static IReadOnlyList<SupportedGame> Link(IReadOnlyList<SupportedGame> games)
    {
        foreach (SupportedGame game in games)
            foreach (GameEditionInfo edition in game.Editions)
                edition.Game = game;
        return games;
    }

    /// <summary>
    /// The start page's groups: per game, each edition whose configured path is valid, then the
    /// recent installations that aren't one of those. A game with neither gets one entry that is not
    /// set up, which points at Settings.
    /// </summary>
    public static IReadOnlyList<(SupportedGame Game, IReadOnlyList<LauncherEntry> Entries)> Launcher(
        Func<string, string> configuredPath, IEnumerable<string> recentPaths)
    {
        var groups = new List<(SupportedGame, IReadOnlyList<LauncherEntry>)>();
        string[] recents = [.. recentPaths];
        foreach (SupportedGame game in Games)
        {
            var entries = new List<LauncherEntry>();
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameEditionInfo edition in game.Editions)
            {
                string path = configuredPath(edition.Id);
                if (GameSettings.IsGameRoot(path, edition.Id) && shown.Add(Normalize(path)))
                    entries.Add(new LauncherEntry(game, edition.Id, edition.Title, path));
            }
            foreach (string path in recents)
            {
                string edition = GameSettings.GameEditionForPath(path);
                if (game.Editions.FirstOrDefault(candidate => candidate.Id == edition) is not { } info || !shown.Add(Normalize(path)))
                    continue;
                // Named for its edition, as a configured one is; by its folder only when that name is already taken.
                string title = entries.Any(entry => entry.Title == info.Title) ? System.IO.Path.GetFileName(Normalize(path)) : info.Title;
                entries.Add(new LauncherEntry(game, edition, title, path));
            }
            if (entries.Count == 0)
                entries.Add(new LauncherEntry(game, "", "Not set up", ""));
            groups.Add((game, entries));
        }
        return groups;
    }

    private static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
}
