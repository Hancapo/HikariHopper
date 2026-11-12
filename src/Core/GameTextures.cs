using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CommonFury.Hashing;
using FiveFury;
using FiveFury.Assets;
using FiveFury.Authoring;
using FiveFury.Cryptography;

namespace HikariHopper.Core;

/// <summary>
/// Where the game looks for a model's textures beyond the model itself: the texture dictionary its archetype names
/// (the ytyps, and vehicles.meta for vehicles), then that dictionary's parents (gtxd.meta and the maps' manifests), up to
/// the shared ones in levels/gta5/generic/gtxd.rpf, and last the detail maps' mapdetail.ytd; each dictionary as the
/// game streams it, DLC overrides included. RPFs in the game folder the game doesn't mount itself (a mod's, such as
/// fix/touhou.rpf, or OpenIV's mods folder) take part too, over the game's own files.
/// FiveFury works that out from the whole game mounted, which holds over a gigabyte; that is done once in the
/// background when the game loads, and what it gives is kept as a few tables, cached on disk until the game's archives
/// change. FiveFury's own index of what each archive holds and declares is kept beside them, so making the tables again
/// after an archive changes reads only that archive, not the whole game.
/// </summary>
public sealed class GameTextures
{
    // 4: RPFs the game doesn't mount itself are in the tables too (3 also had loose files, which tools' caches in the
    // game folder made stale on every run and which ranked stray copies over the game's own files).
    private const int FormatVersion = 4;

    /// <summary>
    /// Where the RPFs the game doesn't mount itself start to rank: over every file of the game (its DLC packs rank from
    /// 2000), as a mod's do; each a step over the one before it in path order, as equal ranks are ambiguous.
    /// </summary>
    internal const int ExtraArchivePriority = 10_000;

    private static readonly ConcurrentDictionary<string, GameTextures> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly string root;
    private readonly Func<Tables> make;
    private readonly object gate = new();
    private Task<Tables>? loading;

    private GameTextures(string root, GameTarget target, GameCrypto crypto) : this(root, () => LoadTables(root, target, crypto))
    {
    }

    /// <summary>Tables made by <paramref name="make"/>, not shared; for tests.</summary>
    internal GameTextures(string root, Func<Tables> make) => (this.root, this.make) = (root, make);

    /// <summary>Where the tables are cached; tests point it elsewhere.</summary>
    public static string CacheFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariHopper", "Textures");

    /// <summary>The tables of the game at <paramref name="gameRoot"/>, shared by every caller.</summary>
    public static GameTextures ForGame(LoadedGame game) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Root)), path => new GameTextures(path, game.Target, game.Crypto));

    /// <summary>Where FiveFury keeps its index of each archive the tables are made from, for every game.</summary>
    internal static string IndexFolder => Path.Combine(CacheFolder, "Index");

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>Shares tables made by <paramref name="make"/> as the game at <paramref name="gameRoot"/>'s; for tests.</summary>
    internal static GameTextures Share(string gameRoot, Func<Tables> make) =>
        Shared[Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot))] = new GameTextures(Path.GetFullPath(gameRoot), make);

    /// <summary>
    /// Waits while the tables of a game whose folder holds <paramref name="path"/> are being made: that reads the game's
    /// RPFs, mods' included, and holds each open until it's done, and Windows doesn't replace, rename or delete an open
    /// file. Making them takes seconds on a real game, and is only done when its archives changed since the tables
    /// were cached. Returns at once for anything but an RPF or a folder, which may hold some.
    /// </summary>
    public static void WaitForArchiveReaders(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!full.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(full))
            return;
        foreach (GameTextures textures in Shared.Values)
        {
            string root = Path.TrimEndingDirectorySeparator(textures.root);
            bool inside = full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                // A folder over the game's holds its archives too.
                || root.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            Task? running;
            lock (textures.gate)
                running = textures.loading;
            if (!inside || running is null)
                continue;
            // Whether the tables could be made doesn't matter here, only that nothing reads the archives any more.
            Task.WhenAny(running).Wait();
        }
    }

    /// <summary>The game folder.</summary>
    public string Root => root;

    /// <summary>
    /// Starts reading the tables in the background, from the cache when the game's archives haven't changed since,
    /// else from the game; does nothing while that runs or once it's done.
    /// </summary>
    public Task Load()
    {
        lock (gate)
            return loading ??= Task.Run(make);
    }

    /// <summary>Whether the tables are ready.</summary>
    public bool Ready => loading is { IsCompletedSuccessfully: true };

    /// <summary>
    /// The dictionaries the game looks in for <paramref name="modelName"/>'s textures (a .ydr, .yft or .ydd name), in
    /// the order it looks: the ones its archetypes name, then their parents, nearest first, then mapdetail.ytd, and for
    /// drawable dictionaries and fragments the peds' comp_peds_generic.ytd; each as
    /// a path from the game folder, RPFs included. Waits for the tables; none when they couldn't be made, whatever stopped them: the tables
    /// only add places to look, so they never stop a model from opening. A failed attempt is kept, not repeated per model.
    /// </summary>
    public async Task<IReadOnlyList<GameDictionary>> DictionariesFor(string modelName)
    {
        try
        {
            await Load().ConfigureAwait(false);
            return loading!.Result.For(modelName);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static Tables LoadTables(string root, GameTarget target, GameCrypto crypto)
    {
        byte[] stamp = Stamp(root, target);
        string cache = Path.Combine(CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16] + ".bin");
        try
        {
            if (Tables.Read(cache, stamp) is { } cached)
                return cached;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
        }
        Tables made = Tables.Build(root, target, crypto, new GameIndexCache(IndexFolder));
        try
        {
            Directory.CreateDirectory(CacheFolder);
            made.Write(cache, stamp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        return made;
    }

    /// <summary>What the tables are made from: every RPF of the game, by path, size and time written.</summary>
    internal static byte[] Stamp(string root, GameTarget target)
    {
        var text = new StringBuilder($"{FormatVersion}|{target}\n");
        foreach (string file in Directory.EnumerateFiles(root, "*.rpf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            text.Append(Path.GetRelativePath(root, file).ToUpperInvariant()).Append('|').Append(info.Length).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>A FiveFury source path (the RPF on disk, then each level inside it after "!/") as a path from the game folder.</summary>
    internal static string? GamePath(string root, string sourcePath)
    {
        string[] levels = sourcePath.Split("!/");
        string disk = levels[0];
        if (!disk.StartsWith(root, StringComparison.OrdinalIgnoreCase) || disk.Length <= root.Length)
            return null;
        string relative = disk[root.Length..].TrimStart('\\', '/').Replace('\\', '/');
        return string.Join('/', [relative, .. levels[1..]]);
    }

    /// <summary>What <see cref="GameTextures"/> keeps of the game: each dictionary's file, its parent, and the dictionaries
    /// each model's archetypes name, all by name hash.</summary>
    internal sealed class Tables
    {
        // Dictionary name hash → its file as the game streams it, and that file's size.
        public Dictionary<uint, (string Path, long Size)> Files { get; } = [];
        // Dictionary → its parent.
        public Dictionary<uint, uint> Parents { get; } = [];
        // Model name hash → the dictionaries its archetypes name.
        public Dictionary<uint, uint[]> Models { get; } = [];

        public IReadOnlyList<GameDictionary> For(string modelName)
        {
            string stem = Path.GetFileNameWithoutExtension(modelName);
            if (!Models.TryGetValue(new Hash32(stem).Value, out uint[]? declared)
                // A vehicle's high-detail model shares the vehicle's archetype.
                && !(stem.EndsWith("_hi", StringComparison.OrdinalIgnoreCase) && Models.TryGetValue(new Hash32(stem[..^3]).Value, out declared)))
                declared = [];
            // As FiveFury's ResolveTexture: the declared dictionaries, then their ancestors by how far up they are.
            var order = new List<uint>(declared);
            var depth = new Dictionary<uint, int>();
            foreach (uint start in declared)
            {
                var seen = new HashSet<uint> { start };
                uint current = start;
                for (int level = 1; Parents.TryGetValue(current, out uint parent) && seen.Add(parent); level++)
                {
                    if (!depth.TryGetValue(parent, out int known) || level < known)
                        depth[parent] = level;
                    current = parent;
                }
            }
            order.AddRange(depth.Where(pair => !declared.Contains(pair.Key)).OrderBy(pair => pair.Value).Select(pair => pair.Key));
            // Last, the dictionary the game keeps loaded for every drawable's detail maps (ENV_NOISE_*, ENV_SMOOTH_*...):
            // platform:/textures/mapdetail.ytd, on no archetype's parent chain.
            order.Add(MapDetail);
            // And for drawable dictionaries and fragments, which peds are, the peds' resident dictionary (peds.ymt's
            // residentTxd), which the game keeps loaded from startup: the hair's noise maps and the dummy maps are there.
            if (Path.GetExtension(modelName).ToLowerInvariant() is ".ydd" or ".yft")
                order.Add(PedResident);
            return [.. order.Distinct().Where(Files.ContainsKey).Select(hash => new GameDictionary(Files[hash].Path, Files[hash].Size))];
        }

        private static readonly uint MapDetail = new Hash32("mapdetail").Value;
        private static readonly uint PedResident = new Hash32("comp_peds_generic").Value;

        /// <summary>
        /// The tables, from the whole game mounted; with <paramref name="index"/>, FiveFury takes each archive it has seen
        /// unchanged from there, which gives the same tables in under half the time.
        /// </summary>
        public static Tables Build(string root, GameTarget target, GameCrypto crypto, GameIndexCache? index = null)
        {
            var tables = new Tables();
            using GameInstallation game = GameInstallation.Open(root, target, crypto, new GameInstallationOptions { IndexCache = index });
            // The RPFs the game doesn't mount itself, over its own files; one that can't be read is left out.
            var mounted = new HashSet<string>(game.FileSystem.Mounts.Select(mount => mount.SourcePath), StringComparer.OrdinalIgnoreCase);
            int priority = ExtraArchivePriority;
            foreach (string archive in ExtraArchives(root, mounted))
            {
                try
                {
                    game.FileSystem.MountArchive(archive, "", priority++, includeInAssetLookup: true);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException or EndOfStreamException)
                {
                }
            }
            using var cache = new GameFileCache(game.FileSystem);
            cache.AssetTextures.Rebuild();
            cache.TextureGraph.Rebuild();
            GameFileSystem files = game.FileSystem;
            foreach (Hash32 name in files.EnumerateFiles(GameFileType.Ytd).Select(file => file.NameHash).Distinct())
                if (files.Resolve(name, GameFileType.Ytd).Value is { } winner && GamePath(root, winner.SourcePath) is { } path)
                    tables.Files[name.Value] = (path, winner.Size);
            var dictionaries = new HashSet<uint>(tables.Files.Keys);
            foreach (GameFileType type in new[] { GameFileType.Ydr, GameFileType.Yft, GameFileType.Ydd })
                foreach (Hash32 model in files.EnumerateFiles(type).Select(file => file.NameHash).Distinct())
                {
                    uint[] declared = [.. cache.AssetTextures.Find(model).Select(value => value.Dictionary.Value).Distinct()];
                    if (declared.Length == 0)
                        continue;
                    tables.Models[model.Value] = declared;
                    dictionaries.UnionWith(declared);
                }
            // Each dictionary's parent, and theirs, as far up as the chains go.
            var pending = new Queue<uint>(dictionaries);
            while (pending.TryDequeue(out uint child))
                if (!tables.Parents.ContainsKey(child) && cache.TextureGraph.GetParent(new Hash32(child)) is { Status: ResolutionStatus.Resolved, Value: { } edge })
                {
                    tables.Parents[child] = edge.ParentHash.Value;
                    pending.Enqueue(edge.ParentHash.Value);
                }
            return tables;
        }

        /// <summary>
        /// The RPFs under the game folder the game doesn't mount itself (<paramref name="mounted"/> are those it does), in
        /// path order: a mod's anywhere in the folder, and OpenIV's mods folder. DLC packs are left to the game's list,
        /// which says which of them it loads, and the game's audio (x64/audio) holds no models or textures.
        /// </summary>
        internal static IReadOnlyList<string> ExtraArchives(string root, IReadOnlySet<string> mounted)
        {
            var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            bool Kept(string path)
            {
                string[] parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
                return !parts.Contains("dlcpacks", StringComparer.OrdinalIgnoreCase)
                    && !(parts.Length > 2 && string.Equals(parts[0], "x64", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(parts[1], "audio", StringComparison.OrdinalIgnoreCase));
            }
            return [.. Directory.EnumerateFiles(root, "*.rpf", enumeration)
                .Select(Path.GetFullPath)
                .Where(path => !mounted.Contains(path) && Kept(path))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }

        public void Write(string path, byte[] stamp)
        {
            string temporary = path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
            {
                writer.Write("HHTX"u8);
                writer.Write(FormatVersion);
                writer.Write(stamp);
                writer.Write(Files.Count);
                foreach (var (hash, (file, size)) in Files)
                {
                    writer.Write(hash);
                    writer.Write(file);
                    writer.Write(size);
                }
                writer.Write(Parents.Count);
                foreach (var (child, parent) in Parents)
                {
                    writer.Write(child);
                    writer.Write(parent);
                }
                writer.Write(Models.Count);
                foreach (var (model, declared) in Models)
                {
                    writer.Write(model);
                    writer.Write(declared.Length);
                    foreach (uint dictionary in declared)
                        writer.Write(dictionary);
                }
            }
            File.Move(temporary, path, overwrite: true);
        }

        /// <summary>The cached tables, or null when there are none or the game has changed since.</summary>
        public static Tables? Read(string path, byte[] stamp)
        {
            if (!File.Exists(path))
                return null;
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            if (!reader.ReadBytes(4).AsSpan().SequenceEqual("HHTX"u8) || reader.ReadInt32() != FormatVersion
                || !reader.ReadBytes(stamp.Length).AsSpan().SequenceEqual(stamp))
                return null;
            var tables = new Tables();
            for (int i = reader.ReadInt32(); i > 0; i--)
                tables.Files[reader.ReadUInt32()] = (reader.ReadString(), reader.ReadInt64());
            for (int i = reader.ReadInt32(); i > 0; i--)
                tables.Parents[reader.ReadUInt32()] = reader.ReadUInt32();
            for (int i = reader.ReadInt32(); i > 0; i--)
            {
                uint model = reader.ReadUInt32();
                int count = reader.ReadInt32();
                if (count is < 0 or > 1024)
                    throw new InvalidDataException("Corrupt texture tables.");
                var declared = new uint[count];
                for (int j = 0; j < count; j++)
                    declared[j] = reader.ReadUInt32();
                tables.Models[model] = declared;
            }
            return tables;
        }
    }
}

/// <summary>A texture dictionary of the game: its path from the game folder, RPFs included, and its size.</summary>
public sealed record GameDictionary(string Path, long Size);
