using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RedFury.Rpf;
using Jenkins = RedFury.Hashing.Jenkins;

namespace HikariHopper.Core;

/// <summary>
/// Where Red Dead Redemption 2 keeps its models' textures beyond the models themselves, after RedFury's
/// RpfTextureCatalog: every texture dictionary in its RPFs by name hash, the archetypes' dictionaries (from its YTYPs),
/// the dictionaries' parents (gtxd and the manifests' TXD relationships) and the high-definition ones the manifests bind
/// to an asset. Each top-level RPF is read on a thread of its own, its YTYPs' bindings as it's read, and what it holds
/// is cached on disk until it changes, so a game is read whole once, then in a moment.
/// </summary>
internal sealed class Rdr2TextureIndex
{
    // The resource extension of texture dictionaries and of YTYPs in RPF8.
    private const byte DictionaryExtension = 5, YtypExtension = 74;

    private static readonly ConcurrentDictionary<string, Rdr2TextureIndex> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly string root;
    private readonly Rdr2Crypto crypto;
    private readonly object gate = new();
    private Task<Index>? loading;

    internal Rdr2TextureIndex(string root, Rdr2Crypto crypto) => (this.root, this.crypto) = (root, crypto);

    /// <summary>The index of the RDR2 game at <paramref name="root"/>, shared by every caller.</summary>
    public static Rdr2TextureIndex ForGame(string root, Rdr2Crypto crypto) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), path => new Rdr2TextureIndex(path, crypto));

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>The game folder.</summary>
    public string Root => root;

    /// <summary>Starts reading the index in the background; does nothing while that runs or once it's done.</summary>
    public Task Load() => Loaded();

    private Task<Index> Loaded()
    {
        lock (gate)
            return loading ??= Task.Run(Read);
    }

    /// <summary>What a cache must be made with to be read.</summary>
    private const string Version = "rdr2-textures-1";

    /// <summary>Where the index is cached: beside GTA V's texture tables, which the settings' cache size and deletion cover.</summary>
    internal string CachePath =>
        Path.Combine(GameTextures.CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16] + "-rdr2.bin");

    /// <summary>
    /// The game's RPFs as RedFury mounts them, as paths from the game folder: base archives before the numbered update
    /// archives, each in natural order, a later one overriding an earlier one.
    /// </summary>
    internal static IReadOnlyList<string> Archives(string root)
    {
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        System.Globalization.CompareInfo compare = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        return [.. Directory.EnumerateFiles(root, "*.rpf", enumeration)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .OrderBy(path => Path.GetFileName(path).StartsWith("update", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(path => path, Comparer<string>.Create((a, b) => compare.Compare(a, b,
                System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.NumericOrdering)))];
    }

    private Index Read()
    {
        string cachePath = CachePath, cacheFolder = Path.GetDirectoryName(cachePath)!;
        Dictionary<string, Part> cached = [];
        try
        {
            cached = ReadCache(cachePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
        }
        IReadOnlyList<string> archives = Archives(root);
        var parts = new ConcurrentDictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
        var stale = new List<(string Path, long Length, long Written)>();
        foreach (string archive in archives)
        {
            FileInfo info = new(Path.Combine(root, archive));
            if (!info.Exists)
                continue;
            if (cached.TryGetValue(archive.ToUpperInvariant(), out Part? part) && part.Length == info.Length && part.Written == info.LastWriteTimeUtc.Ticks)
                parts[archive] = part;
            else
                stale.Add((archive, info.Length, info.LastWriteTimeUtc.Ticks));
        }
        bool fresh = stale.Count > 0;
        // The biggest first, so the last to finish isn't a big one started late.
        Parallel.ForEach(stale.OrderByDescending(archive => archive.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            archive => parts[archive.Path] = Scan(Path.Combine(root, archive.Path), archive.Path, archive.Length, archive.Written, crypto));
        // In mount order.
        Part[] mounted = [.. archives.Where(parts.ContainsKey).Select(archive => parts[archive])];
        if (fresh || cached.Count != mounted.Length)
        {
            try
            {
                Directory.CreateDirectory(cacheFolder);
                // An RPF that couldn't be read isn't kept, so the next run reads it again.
                WriteCache(cachePath, [.. mounted.Where(part => !part.Failed)]);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return new Index(mounted);
    }

    /// <summary>A dictionary in an RPF: its name hash, the entries leading to it from the RPF's root, its size.</summary>
    internal sealed record Held(uint Hash, int[] Indices, long Size);

    /// <summary>A YTYP in an RPF, by name hash, and the dictionary each archetype or asset it declares uses.</summary>
    internal sealed record Ytyp(uint Hash, (uint Asset, uint Dictionary)[] Bindings);

    /// <summary>
    /// What an RPF holds (<paramref name="Archive"/>, from the game folder) as it was at that size and time written: its
    /// dictionaries, YTYPs, dictionary parents (child, parent, in the order read) and high-definition bindings (the
    /// asset, its kind, the dictionary).
    /// </summary>
    internal sealed record Part(string Archive, long Length, long Written, Held[] Dictionaries, Ytyp[] Ytyps,
        (uint Child, uint Parent)[] Parents, (uint Asset, byte Kind, uint Dictionary)[] HighDefinition, bool Failed = false);

    /// <summary>Reads what an RPF holds, its nested RPFs too; one that can't be read is a failed part.</summary>
    internal static Part Scan(string path, string archive, long length, long written, Rdr2Crypto crypto)
    {
        var dictionaries = new List<Held>();
        var ytyps = new List<Ytyp>();
        var parents = new List<(uint, uint)>();
        var high = new List<(uint, byte, uint)>();
        try
        {
            using RpfArchive opened = RpfArchive.Open(path, crypto);
            Visit(opened.Entries, []);
            return new Part(archive, length, written, [.. dictionaries], [.. ytyps], [.. parents], [.. high]);
        }
        catch (Exception error) when (Recoverable(error))
        {
            return new Part(archive, length, written, [], [], [], [], Failed: true);
        }

        void Visit(IEnumerable<RpfEntry> entries, int[] at)
        {
            if (at.Length >= 32)
                throw new InvalidDataException("Archive nesting exceeds the index's depth limit.");
            foreach (RpfEntry entry in entries)
            {
                RpfRecord record = entry.Record;
                try
                {
                    if (record.IsResource)
                    {
                        if (record.ExtensionId == DictionaryExtension)
                            dictionaries.Add(new Held(record.NameHash, [.. at, entry.Index], (long)record.LogicalSize));
                        else if (record.ExtensionId == YtypExtension)
                            ytyps.Add(new Ytyp(record.NameHash, [.. RedFury.Ytyp.Ytyp.TextureBindingsFromResource(entry.ReadResource())
                                .SelectMany(binding => new[] { binding.ArchetypeHash, binding.AssetHash }
                                    .Where(asset => asset != 0 && binding.TextureDictionaryHash != 0)
                                    .Select(asset => (asset, binding.TextureDictionaryHash)))
                                .Distinct()]));
                        continue;
                    }
                    if (record.ExtensionId == 0)
                    {
                        using RpfArchive child = entry.OpenArchive();
                        Visit(child.Entries, [.. at, entry.Index]);
                    }
                    else if (record.ExtensionId == 1)
                    {
                        RedFury.Ymf.YmfManifest manifest = RedFury.Ymf.Ymf.FromEntry(entry).Manifest;
                        foreach (RedFury.Ymf.YmfTxdRelationship edge in manifest.TxdRelationships)
                            parents.Add((Jenkins.Hash(edge.Child), Jenkins.Hash(edge.Parent)));
                        foreach (RedFury.Ymf.YmfHdTextureBinding binding in manifest.HdTextureBindings)
                            if (!string.IsNullOrWhiteSpace(binding.TargetAsset) && !string.IsNullOrWhiteSpace(binding.HdTextureDictionary))
                                high.Add((Jenkins.Hash(binding.TargetAsset), (byte)binding.AssetType, Jenkins.Hash(binding.HdTextureDictionary)));
                    }
                    else if (record.ExtensionId == 11 && record.NameHash == RedFury.Ymt.Gtxd.NameHash
                        && RedFury.Ymt.Ymt.FromEntry(entry).Gtxd is { } gtxd)
                    {
                        foreach (RedFury.Ymt.GtxdRelationship edge in gtxd.Relationships)
                            parents.Add((Jenkins.Hash(edge.Child), Jenkins.Hash(edge.Parent)));
                    }
                }
                catch (Exception error) when (Recoverable(error))
                {
                }
            }
        }
    }

    private static bool Recoverable(Exception error) =>
        error is IOException or InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException or UnauthorizedAccessException;

    /// <summary>
    /// The dictionaries a model's textures are looked for in, in the order RedFury's TextureBindingPlan looks: the
    /// high-definition ones the manifests bind to it and to its dictionaries' parents, then the ones its archetypes
    /// declare and the one sharing its name, each followed by its parents. Each is its RPF's path from the game folder,
    /// the entries leading to it, and its size. Waits for the index.
    /// </summary>
    /// <param name="asset">The model's name hash.</param>
    /// <param name="extension">Its resource extension: 2 a drawable, 3 a fragment, 4 a drawable dictionary.</param>
    public async Task<IReadOnlyList<(string Archive, int[] Indices, long Size)>> DictionariesFor(uint asset, byte extension)
    {
        Index index = await Loaded().ConfigureAwait(false);
        return index.DictionariesFor(asset, extension);
    }

    /// <summary>The dictionaries for a model in RPFs read as <paramref name="mounted"/>, in mount order; for tests.</summary>
    internal static IReadOnlyList<(string Archive, int[] Indices, long Size)> DictionariesFor(Part[] mounted, uint asset, byte extension) =>
        new Index(mounted).DictionariesFor(asset, extension);

    /// <summary>The index read and merged in mount order.</summary>
    private sealed class Index
    {
        private readonly Dictionary<uint, (string Archive, Held Held)> dictionaries = [];
        private readonly Dictionary<uint, uint> parents = [];
        private readonly Dictionary<(uint, byte), List<uint>> high = [];
        private readonly Dictionary<uint, List<uint>> archetypes = [];

        public Index(Part[] mounted)
        {
            foreach (Part part in mounted)
            {
                // A later RPF's dictionary of a name overrides an earlier one's; so does its parent for a child.
                foreach (Held held in part.Dictionaries)
                    dictionaries[held.Hash] = (part.Archive, held);
                foreach (var (child, parent) in part.Parents)
                    parents[child] = parent;
                foreach (var (asset, kind, dictionary) in part.HighDefinition)
                {
                    if (!high.TryGetValue((asset, kind), out List<uint>? values))
                        high.Add((asset, kind), values = []);
                    if (!values.Contains(dictionary))
                        values.Add(dictionary);
                }
            }
            // Only the effective YTYP of a name declares archetypes, the latest first, as RedFury indexes them.
            var seen = new HashSet<uint>();
            foreach (Part part in mounted.Reverse())
                foreach (Ytyp ytyp in part.Ytyps.Reverse())
                {
                    if (!seen.Add(ytyp.Hash))
                        continue;
                    foreach (var (asset, dictionary) in ytyp.Bindings)
                    {
                        if (!archetypes.TryGetValue(asset, out List<uint>? values))
                            archetypes.Add(asset, values = []);
                        if (!values.Contains(dictionary))
                            values.Add(dictionary);
                    }
                }
        }

        // A dictionary, then its parents; stopping at a cycle.
        private List<uint> Chain(uint child)
        {
            var chain = new List<uint>();
            var visited = new HashSet<uint>();
            while (child != 0 && chain.Count < 64 && visited.Add(child))
            {
                chain.Add(child);
                if (!parents.TryGetValue(child, out child))
                    break;
            }
            return chain;
        }

        public IReadOnlyList<(string Archive, int[] Indices, long Size)> DictionariesFor(uint asset, byte extension)
        {
            var baseRoots = new List<uint>(archetypes.GetValueOrDefault(asset) ?? []) { asset };
            // RedFury's YmfAssetType: 0 texture dictionary, 1 drawable, 2 fragment, 3 drawable dictionary.
            byte kind = extension switch { 2 => 1, 3 => 2, 4 => 3, _ => 0 };
            var highRoots = new List<uint>(high.GetValueOrDefault((asset, kind)) ?? []);
            foreach (uint baseRoot in baseRoots)
                foreach (uint ancestor in Chain(baseRoot))
                    if (high.TryGetValue((ancestor, 0), out List<uint>? more))
                        highRoots.AddRange(more);
            var order = highRoots.SelectMany(Chain).Concat(baseRoots.SelectMany(Chain)).Distinct();
            return [.. order.Where(dictionaries.ContainsKey).Select(hash => dictionaries[hash])
                .Select(found => (found.Archive, found.Held.Indices, found.Held.Size))];
        }
    }

    internal static void WriteCache(string path, IReadOnlyList<Part> parts)
    {
        string temporary = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
        {
            writer.Write("HHR2"u8);
            writer.Write(Version);
            writer.Write(parts.Count);
            foreach (Part part in parts)
            {
                writer.Write(part.Archive);
                writer.Write(part.Length);
                writer.Write(part.Written);
                writer.Write(part.Dictionaries.Length);
                foreach (Held held in part.Dictionaries)
                {
                    writer.Write(held.Hash);
                    writer.Write(held.Size);
                    writer.Write((byte)held.Indices.Length);
                    foreach (int index in held.Indices)
                        writer.Write(index);
                }
                writer.Write(part.Ytyps.Length);
                foreach (Ytyp ytyp in part.Ytyps)
                {
                    writer.Write(ytyp.Hash);
                    writer.Write(ytyp.Bindings.Length);
                    foreach (var (asset, dictionary) in ytyp.Bindings)
                    {
                        writer.Write(asset);
                        writer.Write(dictionary);
                    }
                }
                writer.Write(part.Parents.Length);
                foreach (var (child, parent) in part.Parents)
                {
                    writer.Write(child);
                    writer.Write(parent);
                }
                writer.Write(part.HighDefinition.Length);
                foreach (var (asset, kind, dictionary) in part.HighDefinition)
                {
                    writer.Write(asset);
                    writer.Write(kind);
                    writer.Write(dictionary);
                }
            }
        }
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The cached parts by their RPF's path, upper case; none when there's no cache, or it's of another version.</summary>
    internal static Dictionary<string, Part> ReadCache(string path)
    {
        var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return parts;
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("HHR2"u8) || reader.ReadString() != Version)
            return parts;
        int Count(int most)
        {
            int count = reader.ReadInt32();
            return count < 0 || count > most ? throw new InvalidDataException("Corrupt RDR2 texture index.") : count;
        }
        for (int i = Count(100_000); i > 0; i--)
        {
            string archive = reader.ReadString();
            long length = reader.ReadInt64(), written = reader.ReadInt64();
            var dictionaries = new Held[Count(10_000_000)];
            for (int j = 0; j < dictionaries.Length; j++)
            {
                uint hash = reader.ReadUInt32();
                long size = reader.ReadInt64();
                var indices = new int[reader.ReadByte()];
                for (int k = 0; k < indices.Length; k++)
                    indices[k] = reader.ReadInt32();
                dictionaries[j] = new Held(hash, indices, size);
            }
            var ytyps = new Ytyp[Count(10_000_000)];
            for (int j = 0; j < ytyps.Length; j++)
            {
                uint hash = reader.ReadUInt32();
                var bindings = new (uint, uint)[Count(10_000_000)];
                for (int k = 0; k < bindings.Length; k++)
                    bindings[k] = (reader.ReadUInt32(), reader.ReadUInt32());
                ytyps[j] = new Ytyp(hash, bindings);
            }
            var parentEdges = new (uint, uint)[Count(10_000_000)];
            for (int j = 0; j < parentEdges.Length; j++)
                parentEdges[j] = (reader.ReadUInt32(), reader.ReadUInt32());
            var high = new (uint, byte, uint)[Count(10_000_000)];
            for (int j = 0; j < high.Length; j++)
                high[j] = (reader.ReadUInt32(), reader.ReadByte(), reader.ReadUInt32());
            parts[archive.ToUpperInvariant()] = new Part(archive, length, written, dictionaries, ytyps, parentEdges, high);
        }
        return parts;
    }

    /// <summary>
    /// A dictionary's bytes, as a standalone RSC8 resource: opened from its RPF through the entries leading to it.
    /// </summary>
    public byte[] ReadDictionary(string archive, IReadOnlyList<int> indices)
    {
        var opened = new List<RpfArchive>();
        try
        {
            opened.Add(RpfArchive.Open(Path.Combine(root, archive), crypto));
            for (int i = 0; i < indices.Count - 1; i++)
                opened.Add(opened[^1].Entries[indices[i]].OpenArchive());
            return opened[^1].Entries[indices[^1]].ReadResource().Write();
        }
        finally
        {
            foreach (RpfArchive archiveOpened in Enumerable.Reverse(opened))
                archiveOpened.Dispose();
        }
    }
}
