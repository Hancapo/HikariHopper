using RedFury.Rpf;
using RedFury.Textures;

namespace HikariHopper.Core;

/// <summary>A file of the game, as the explorer has it: its path from the game folder, RPFs as folders, and its size.</summary>
internal sealed record WorldFile(string Path, long Size)
{
    /// <summary>Its name, without its folders.</summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>Its name without its extension.</summary>
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Name);
}

/// <summary>
/// Where the world's assets and maps are among the game's files: a catalog location (an RPF and the entries leading to
/// the file, through nested RPFs) as the path the explorer shows it at, each entry named as the explorer names it
/// (RedFury's name index, with its format's suffix).
/// </summary>
internal static class Rdr2WorldFiles
{
    /// <summary>The asset or map of this hash and extension (2 drawable, 3 fragment, 4 dictionary, 73 map); null when there's none.</summary>
    public static WorldFile? Locate(Rdr2WorldIndex index, string gameRoot, uint hash, byte extension)
    {
        TextureAssetLocation? location;
        // The catalog is the streamer's worker's too, under the same lock.
        lock (index.Reads)
            location = index.Assets.Find(hash, extension).LastOrDefault();
        if (location is null)
            return null;
        try
        {
            var parts = new List<string> { Path.GetRelativePath(gameRoot, location.RootPath).Replace('\\', '/') };
            RpfArchive archive = RpfArchive.Open(location.RootPath, index.Crypto);
            var opened = new List<RpfArchive> { archive };
            try
            {
                long size = 0;
                for (int level = 0; level < location.Indices.Count; level++)
                {
                    RpfEntry entry = archive.Entries[location.Indices[level]];
                    parts.Add(NameOf(entry.Record));
                    if (level < location.Indices.Count - 1)
                    {
                        archive = entry.OpenArchive();
                        opened.Add(archive);
                    }
                    else
                        size = (long)entry.Record.LogicalSize;
                }
                return new WorldFile(string.Join('/', parts), size);
            }
            finally
            {
                foreach (RpfArchive open in opened)
                    open.Dispose();
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or MissingRpfKeyException)
        {
            return null;
        }
    }

    /// <summary>A name the game is known to use for this hash, or the hash itself as the explorer writes it.</summary>
    public static string NameOf(uint hash) => RpfNameIndex.Default.Resolve(hash) is { Count: > 0 } names ? names[0].Name : $"0x{hash:X8}";

    // An entry's name as RedFury's ArchiveDirectory gives it: its known name (with its folders) or its hash, and its
    // format's suffix when the name lacks it.
    private static string NameOf(RpfRecord record)
    {
        string name = NameOf(record.NameHash);
        RpfFormat format = RpfFormat.Classify(record, name);
        if (!name.EndsWith(format.Suffix, StringComparison.OrdinalIgnoreCase))
            name += format.Suffix;
        return name.Replace('\\', '/').Trim('/');
    }
}
