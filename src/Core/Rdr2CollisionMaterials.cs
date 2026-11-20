using System.Collections.Concurrent;
using RedFury.Collisions;
using RedFury.Rpf;

namespace HikariHopper.Core;

/// <summary>
/// Red Dead Redemption 2's collision materials, from its materials.meta (the update's copy over the base one), as
/// RedFury's explorer reads them: their names and filter groups, by the identity a collision stores. Read once per game.
/// </summary>
internal static class Rdr2CollisionMaterials
{
    private static readonly ConcurrentDictionary<string, Task<CollisionMaterialCatalog?>> Shared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the game keeps its materials.meta, the update's first.</summary>
    internal static readonly (string Archive, string Path)[] Places =
        [("update_1.rpf", "common/data/materials/materials.meta"), ("common_0.rpf", "data/materials/materials.meta")];

    /// <summary>The game's collision materials, read in the background once; null when neither copy can be read.</summary>
    public static Task<CollisionMaterialCatalog?> ForGame(string root, Rdr2Crypto crypto) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), path => Task.Run(() => Read(path, crypto)));

    private static CollisionMaterialCatalog? Read(string root, Rdr2Crypto crypto)
    {
        foreach (var (file, path) in Places)
        {
            string archivePath = Path.Combine(root, file);
            if (!File.Exists(archivePath))
                continue;
            try
            {
                using RpfArchive archive = RpfArchive.Open(archivePath, crypto);
                uint hash = RedFury.Hashing.Jenkins.Hash(path);
                if (archive.Entries.FirstOrDefault(entry => entry.Record.NameHash == hash) is not { } entry)
                    continue;
                using var stream = new MemoryStream(entry.Read());
                return CollisionMaterialCatalog.Read(stream);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or System.Xml.XmlException
                or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }
}
