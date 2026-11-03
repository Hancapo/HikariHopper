using System.Runtime.Versioning;

namespace HikariHopper.Core;

/// <summary>One edition of a supported game, keyed like the settings (<see cref="GameSettings"/>).</summary>
public sealed record GameEditionInfo(string Id, string Title);

/// <summary>A game HikariHopper can open, with its editions in the order the launcher lists them.</summary>
public sealed record SupportedGame(string Id, string Title, IReadOnlyList<GameEditionInfo> Editions);

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

    public static IReadOnlyList<SupportedGame> Games { get; } =
    [
        new(GtaV, "Grand Theft Auto V",
        [
            new(GameSettings.EnhancedEdition, "Enhanced"),
            new(GameSettings.LegacyEdition, "Legacy"),
        ]),
    ];

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
                if (game.Editions.Any(candidate => candidate.Id == edition) && shown.Add(Normalize(path)))
                    entries.Add(new LauncherEntry(game, edition, System.IO.Path.GetFileName(Normalize(path)), path));
            }
            if (entries.Count == 0)
                entries.Add(new LauncherEntry(game, "", "Not set up", ""));
            groups.Add((game, entries));
        }
        return groups;
    }

    private static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
}
