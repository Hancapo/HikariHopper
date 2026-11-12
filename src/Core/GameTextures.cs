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
/// FiveFury works that out from the whole game mounted, which holds over a gigabyte; that is done while the game loads,
/// and what it gives is kept as a few tables, cached on disk until the game's archives change. HikariHopper's own saves
/// of a texture dictionary or a model into an RPF keep the cache (<see cref="Rewritten"/>), as they change no table;
/// anything else it changes in an RPF has the tables made again (<see cref="Changed"/>).
/// FiveFury's own index of what each archive holds and declares is kept beside them, so making the tables again
/// after an archive changes reads only that archive, not the whole game.
/// </summary>
public sealed class GameTextures
{
    // 4: RPFs the game doesn't mount itself are in the tables too (3 also had loose files, which tools' caches in the
    // game folder made stale on every run and which ranked stray copies over the game's own files).
    // 5: the game's car colours and each vehicle's combinations of them.
    private const int FormatVersion = 6;

    /// <summary>
    /// Where the RPFs the game doesn't mount itself start to rank: over every file of the game (its DLC packs rank from
    /// 2000), as a mod's do; each a step over the one before it in path order, as equal ranks are ambiguous.
    /// </summary>
    internal const int ExtraArchivePriority = 10_000;

    private static readonly ConcurrentDictionary<string, GameTextures> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly string root;
    private readonly GameTarget target;
    private readonly Func<Tables> build;
    private readonly object gate = new();
    private readonly object writing = new();
    private Task<Tables>? loading;
    // The game's archives as they were when the tables in memory were read or made; null until then.
    private IReadOnlyList<ArchiveState>? madeFrom;
    // Set once HikariHopper changes the game's archives in a way the tables may not hold (see Changed): the tables in
    // memory are as they were before, so no later save keeps the cache with them.
    private bool stale;

    /// <summary>Tables made by <paramref name="build"/> when the cache has none for the game as it is, not shared; for tests.</summary>
    internal GameTextures(string root, GameTarget target, Func<Tables> build) => (this.root, this.target, this.build) = (root, target, build);

    /// <summary>Where the tables are cached; tests point it elsewhere.</summary>
    public static string CacheFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariHopper", "Textures");

    /// <summary>Makes a game's tables from the game itself (its folder, edition and keys); tests replace it.</summary>
    internal static Func<string, GameTarget, GameCrypto, Tables> Builder { get; set; } =
        (root, target, crypto) => Tables.Build(root, target, crypto, new GameIndexCache(IndexFolder));

    /// <summary>The tables of <paramref name="game"/>, shared by every caller.</summary>
    public static GameTextures ForGame(LoadedGame game) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Root)),
            path => new GameTextures(path, game.Target, () => Builder(path, game.Target, game.Crypto)));

    /// <summary>Where FiveFury keeps its index of each archive the tables are made from, for every game.</summary>
    internal static string IndexFolder => Path.Combine(CacheFolder, "Index");

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>Shares tables made by <paramref name="make"/> as the game at <paramref name="gameRoot"/>'s; for tests.</summary>
    internal static GameTextures Share(string gameRoot, Func<Tables> make) =>
        Shared[Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot))] = new GameTextures(Path.GetFullPath(gameRoot), GameTarget.Legacy, make);

    /// <summary>
    /// Waits while the tables of a game whose folder holds <paramref name="path"/> are being made: that reads the game's
    /// RPFs, mods' included, and holds each open until it's done, and Windows doesn't replace, rename or delete an open
    /// file. Making them takes seconds on a real game, and is only done when its archives changed since the tables
    /// were cached. Returns at once for anything but an RPF or a folder, which may hold some.
    /// </summary>
    public static void WaitForArchiveReaders(string path)
    {
        foreach (GameTextures textures in Holding(path))
        {
            Task? running;
            lock (textures.gate)
                running = textures.loading;
            if (running is null)
                continue;
            // Whether the tables could be made doesn't matter here, only that nothing reads the archives any more.
            Task.WhenAny(running).Wait();
        }
    }

    /// <summary>
    /// HikariHopper changed <paramref name="path"/>, an RPF on disk (or a folder holding some), other than by saving new
    /// contents over a texture dictionary or a model in it (<see cref="KeepsTables"/>): entries added, removed or
    /// renamed, or another file saved, such as a meta or a ytyp the tables are made from. The tables may no longer hold,
    /// so no later save keeps the cache of a game holding it (<see cref="Rewritten"/>), and they are made again the next
    /// time the game loads.
    /// </summary>
    public static void Changed(string path)
    {
        foreach (GameTextures textures in Holding(path))
            lock (textures.writing)
                textures.stale = true;
    }

    /// <summary>Whether saving new contents over the file <paramref name="entryPath"/> in an RPF leaves the tables as
    /// they are: a texture dictionary or a model, whose contents no table is made from.</summary>
    public static bool KeepsTables(string entryPath) =>
        Path.GetExtension(entryPath).ToLowerInvariant() is ".ytd" or ".ydr" or ".yft" or ".ydd";

    /// <summary>The games whose folder holds <paramref name="path"/>, an RPF or a folder; none for anything else.</summary>
    private static IEnumerable<GameTextures> Holding(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!full.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(full))
            yield break;
        foreach (GameTextures textures in Shared.Values)
        {
            string root = Path.TrimEndingDirectorySeparator(textures.root);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                // A folder over the game's holds its archives too.
                || root.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                yield return textures;
        }
    }

    /// <summary>The game folder.</summary>
    public string Root => root;

    /// <summary>
    /// Starts reading the tables in the background, from the cache when the game's archives haven't changed since,
    /// else from the game, calling <paramref name="building"/> first, as that takes a while; does nothing while that
    /// runs or once it's done.
    /// </summary>
    public Task Load(Action? building = null)
    {
        lock (gate)
            return loading ??= Task.Run(() => LoadTables(building));
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

    /// <summary>
    /// The game's car colours and <paramref name="modelName"/>'s combinations of them (a vehicle's .yft name); none when
    /// the game has no colours, or the tables couldn't be made. A vehicle that carvariations doesn't list has the
    /// colours and no combinations.
    /// </summary>
    public async Task<Rendering.CarPaints?> PaintsFor(string modelName)
    {
        try
        {
            await Load().ConfigureAwait(false);
            return loading!.Result.PaintsFor(modelName);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
    }

    private string CachePath =>
        Path.Combine(CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16] + ".bin");

    private Tables LoadTables(Action? building)
    {
        IReadOnlyList<ArchiveState> archives = Archives(root);
        byte[] stamp = Stamp(archives, target);
        Tables? tables = null;
        try
        {
            tables = Tables.Read(CachePath, stamp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
        }
        if (tables is null)
        {
            building?.Invoke();
            tables = build();
            Keep(tables, stamp);
        }
        lock (writing)
            madeFrom = archives;
        return tables;
    }

    private void Keep(Tables tables, byte[] stamp)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            tables.Write(CachePath, stamp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// HikariHopper wrote <paramref name="archivePath"/>, an RPF on disk, changing only the contents of files in it (a
    /// texture dictionary or a model saved): the tables still hold, so each game's cache that holds that RPF is kept for
    /// the game as it is now, rather than made again the next time the game loads. Not when anything else of the
    /// game's archives changed since its tables were read, nor once HikariHopper changed them otherwise
    /// (<see cref="Changed"/>): they're made again then.
    /// </summary>
    public static Task Rewritten(string archivePath)
    {
        string written = Path.GetFullPath(archivePath);
        GameTextures[] games = [.. Shared.Values.Where(game => written.StartsWith(game.root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))];
        return games.Length == 0 ? Task.CompletedTask : Task.Run(() =>
        {
            foreach (GameTextures game in games)
                game.Restamp(written);
        });
    }

    /// <summary>Keeps the cache for the game as it is now when only <paramref name="archivePath"/> changed since the
    /// tables were read; see <see cref="Rewritten"/>.</summary>
    internal void Restamp(string archivePath)
    {
        lock (writing)
        {
            if (stale || madeFrom is not { } before || loading is not { IsCompletedSuccessfully: true } done)
                return;
            string written = Path.GetRelativePath(root, archivePath).ToUpperInvariant();
            IReadOnlyList<ArchiveState> now;
            try
            {
                now = Archives(root);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return;
            }
            if (now.Count != before.Count)
                return;
            for (int i = 0; i < now.Count; i++)
                if (now[i].Path != before[i].Path || (now[i] != before[i] && now[i].Path != written))
                    return;
            Keep(done.Result, Stamp(now, target));
            madeFrom = now;
        }
    }

    /// <summary>An RPF of the game: its path from the game folder, upper case, its size and when it was written.</summary>
    internal readonly record struct ArchiveState(string Path, long Length, long Written);

    /// <summary>Every RPF of the game, in path order.</summary>
    internal static IReadOnlyList<ArchiveState> Archives(string root) =>
        [.. Directory.EnumerateFiles(root, "*.rpf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).Select(file =>
        {
            var info = new FileInfo(file);
            return new ArchiveState(Path.GetRelativePath(root, file).ToUpperInvariant(), info.Length, info.LastWriteTimeUtc.Ticks);
        })];

    /// <summary>
    /// The build of FiveFury (and of CommonFury, which names its hashes) the tables are made with: what FiveFury finds
    /// changes with it, so tables an older one made are made again. Each changes only with their code, as their builds
    /// are deterministic; tests set it.
    /// </summary>
    internal static string MadeWith { get; set; } =
        $"{typeof(GameFileCache).Assembly.ManifestModule.ModuleVersionId}|{typeof(Hash32).Assembly.ManifestModule.ModuleVersionId}";

    /// <summary>What the tables are made from: every RPF of the game, by path, size and time written, and the FiveFury
    /// that read them (<see cref="MadeWith"/>).</summary>
    internal static byte[] Stamp(string root, GameTarget target) => Stamp(Archives(root), target);

    private static byte[] Stamp(IReadOnlyList<ArchiveState> archives, GameTarget target)
    {
        var text = new StringBuilder($"{FormatVersion}|{target}|{MadeWith}\n");
        foreach (ArchiveState archive in archives)
            text.Append(archive.Path).Append('|').Append(archive.Length).Append('|').Append(archive.Written).Append('\n');
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

        // Car colour number → its name as carcols has it, its colour, and its metallic setting if it has one.
        public Dictionary<int, (string Name, uint Argb, Rendering.CarMetallic? Metallic)> Colours { get; } = [];
        // Vehicle model name hash → its colour combinations (carvariations): each the slots' colour numbers, -1 for none.
        public Dictionary<uint, int[][]> Combinations { get; } = [];
        private IReadOnlyList<Rendering.CarColour>? colours;

        public Rendering.CarPaints? PaintsFor(string modelName)
        {
            if (Colours.Count == 0)
                return null;
            colours ??= [.. Colours.OrderBy(pair => pair.Key).Select(pair =>
                new Rendering.CarColour(pair.Key, Rendering.CarColourNames.Tidy(pair.Value.Name), pair.Value.Argb, Rendering.CarColourNames.Finish(pair.Value.Name), pair.Value.Metallic))];
            string stem = Path.GetFileNameWithoutExtension(modelName);
            if (!Combinations.TryGetValue(new Hash32(stem).Value, out int[][]? combinations)
                // A vehicle's high-detail model is the vehicle's.
                && !(stem.EndsWith("_hi", StringComparison.OrdinalIgnoreCase) && Combinations.TryGetValue(new Hash32(stem[..^3]).Value, out combinations)))
                combinations = [];
            return new(colours, [.. combinations.Select(combination => combination.Select(index => index < 0 ? (int?)null : index).ToArray())]);
        }

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
            // The car colours (carcols) and each vehicle's combinations of them (carvariations), as the game has them now.
            FiveFury.VehicleMeta.VehicleAppearanceIndex vehicles = cache.GetVehicleAppearanceIndex();
            foreach (FiveFury.VehicleMeta.ResolvedVehicleColor colour in vehicles.Colors())
                tables.Colours[colour.Index] = (colour.Name, colour.PackedArgb, colour.Metallic is { } metallic
                    ? new Rendering.CarMetallic(metallic.SpecularIntensity, metallic.SpecularFalloff, metallic.SpecularFresnel) : null);
            foreach (Hash32 model in vehicles.VariationModels)
                if (vehicles.FindVariation(model) is { Colors.Count: > 0 } variation)
                    tables.Combinations[model.Value] = [.. variation.Colors.Select(combination => combination.Indices.ToArray())];
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
                writer.Write(Colours.Count);
                foreach (var (index, (name, argb, metallic)) in Colours)
                {
                    writer.Write(index);
                    writer.Write(name);
                    writer.Write(argb);
                    writer.Write(metallic is not null);
                    if (metallic is not null)
                    {
                        writer.Write(metallic.Intensity);
                        writer.Write(metallic.Falloff);
                        writer.Write(metallic.Fresnel);
                    }
                }
                writer.Write(Combinations.Count);
                foreach (var (model, combinations) in Combinations)
                {
                    writer.Write(model);
                    writer.Write(combinations.Length);
                    foreach (int[] combination in combinations)
                    {
                        writer.Write((byte)combination.Length);
                        foreach (int index in combination)
                            writer.Write(index);
                    }
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
            for (int i = reader.ReadInt32(); i > 0; i--)
                tables.Colours[reader.ReadInt32()] = (reader.ReadString(), reader.ReadUInt32(),
                    reader.ReadBoolean() ? new Rendering.CarMetallic(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) : null);
            for (int i = reader.ReadInt32(); i > 0; i--)
            {
                uint model = reader.ReadUInt32();
                int count = reader.ReadInt32();
                if (count is < 0 or > 1024)
                    throw new InvalidDataException("Corrupt texture tables.");
                var combinations = new int[count][];
                for (int j = 0; j < count; j++)
                {
                    combinations[j] = new int[reader.ReadByte()];
                    for (int k = 0; k < combinations[j].Length; k++)
                        combinations[j][k] = reader.ReadInt32();
                }
                tables.Combinations[model] = combinations;
            }
            return tables;
        }
    }
}

/// <summary>A texture dictionary of the game: its path from the game folder, RPFs included, and its size.</summary>
public sealed record GameDictionary(string Path, long Size);
