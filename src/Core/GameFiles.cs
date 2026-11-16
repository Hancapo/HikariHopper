using System.Text;

namespace HikariHopper.Core;

/// <summary>Data files of a GTA V game, read from its archives: the timecycle's, the water's.</summary>
internal static class GameFiles
{
    /// <summary>
    /// A text file from the first of <paramref name="places"/> that has it (an archive in the game folder and the file's
    /// path in it): the update's, say, before the base game's; null when none has it.
    /// </summary>
    public static string? ReadText(LoadedGame game, IEnumerable<(string Archive, string Path)> places)
    {
        foreach (var (archive, path) in places)
        {
            if (!File.Exists(Path.Combine(game.Root, archive)))
                continue;
            var reader = new RpfProvider();
            try
            {
                reader.ApplyLoadedGame(game);
                reader.OpenArchive(Path.Combine(game.Root, archive));
                if (reader.ArchiveEntryFromRoot(path) is { } entry)
                    return Encoding.UTF8.GetString(reader.ReadEntryBytes(entry));
            }
            finally
            {
                reader.Close();
            }
        }
        return null;
    }
}
