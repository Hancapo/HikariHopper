using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CommonFury.Hashing;
using FiveFury;
using FiveFury.Assets;
using FiveFury.Cryptography;
using FiveFury.Rpf;
using FiveFury.Textures;

namespace HikariHopper.Core;

/// <summary>
/// Which of the game's texture dictionaries hold a texture of a given name: every .ytd in every RPF of the game folder,
/// nested RPFs included, by the names of its textures. A model's textures that neither the dictionaries named for it nor
/// the game's lists of them (<see cref="GameTextures"/>) hold are looked up here, rather than searched for by reading the
/// dictionaries beside it: a vehicle mod part's shared vehicle textures, say, are in a dictionary of the base game.
/// Only the start of each dictionary is read, where its names are, so the whole game takes a second or two, each
/// top-level RPF on a thread of its own; what each RPF holds is cached on disk until that RPF changes, so a game
/// update or a mod's save reads only the RPFs it changed.
/// </summary>
public sealed class GameTextureDirectory
{
    // 1: each RPF's dictionaries with their textures' name hashes.
    private const int FormatVersion = 1;

    // How much of a dictionary is read first for its names: the textures' headers, before their pixels, fit in it for
    // nearly every dictionary of the game; the rest are read again in full.
    private const int FirstRead = 32 * 1024;

    private static readonly ConcurrentDictionary<string, GameTextureDirectory> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly string root;
    private readonly GameTarget target;
    private readonly GameCrypto? crypto;
    private readonly object gate = new();
    private Task<Names>? loading;

    internal GameTextureDirectory(string root, GameTarget target, GameCrypto? crypto) => (this.root, this.target, this.crypto) = (root, target, crypto);

    /// <summary>The directory of <paramref name="game"/>, shared by every caller.</summary>
    public static GameTextureDirectory ForGame(LoadedGame game) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Root)),
            path => new GameTextureDirectory(path, game.Target, game.Crypto));

    /// <summary>Reads what an RPF holds (<see cref="Scan"/>); tests replace it.</summary>
    internal static Func<string, string, long, long, GameTarget, GameCrypto?, Part> Scanner { get; set; } = Scan;

    /// <summary>Forgets every shared instance; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>The game folder.</summary>
    public string Root => root;

    /// <summary>
    /// Starts reading the directory in the background: each RPF's part from the cache when the RPF hasn't changed since,
    /// else from the RPF; does nothing while that runs or once it's done.
    /// </summary>
    public Task Load()
    {
        lock (gate)
            return loading ??= Task.Run(Read);
    }

    /// <summary>Whether the directory is ready.</summary>
    public bool Ready => loading is { IsCompletedSuccessfully: true };

    /// <summary>
    /// The dictionaries that hold a texture named <paramref name="textureName"/>, nearest to <paramref name="nearPath"/>
    /// (a model's path from the game folder, RPFs as folders) first: those in its folder, then its RPF, and so on up,
    /// then a mod's over the game's update over the game's own, as the game takes them; none when no dictionary of the
    /// game holds one; null while the directory isn't ready, or couldn't be read, and when none does but an RPF of the
    /// game couldn't be read, which may hold one.
    /// </summary>
    public IReadOnlyList<GameDictionary>? Holding(string textureName, string? nearPath)
    {
        if (loading is not { IsCompletedSuccessfully: true } done)
            return null;
        IReadOnlyList<GameDictionary> found = done.Result.Holding(textureName, nearPath);
        return found.Count == 0 && !done.Result.Complete ? null : found;
    }

    /// <summary>
    /// Waits while the directory of a game whose folder holds <paramref name="path"/> is being read: that holds each RPF
    /// it reads open meanwhile, and Windows doesn't replace an open file (see <see cref="GameTextures.WaitForArchiveReaders"/>).
    /// </summary>
    internal static void WaitForArchiveReaders(string path)
    {
        foreach (GameTextureDirectory directory in Holding(path))
        {
            Task? running;
            lock (directory.gate)
                running = directory.loading;
            if (running is not null)
                Task.WhenAny(running).Wait();
        }
    }

    /// <summary>
    /// HikariHopper wrote <paramref name="path"/>, an RPF on disk (or a folder holding some): the directory of a game
    /// holding it is read again the next time it's asked for, the RPFs that changed from them, the rest from the cache.
    /// </summary>
    internal static void Changed(string path)
    {
        foreach (GameTextureDirectory directory in Holding(path))
            lock (directory.gate)
                directory.loading = null;
    }

    private static IEnumerable<GameTextureDirectory> Holding(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!full.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(full))
            yield break;
        foreach (GameTextureDirectory directory in Shared.Values)
        {
            string root = Path.TrimEndingDirectorySeparator(directory.root);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                yield return directory;
        }
    }

    /// <summary>Where the directory is cached: beside the texture tables, which the settings' cache size and deletion cover.</summary>
    internal string CachePath =>
        Path.Combine(GameTextures.CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16] + "-names.bin");

    /// <summary>Each RPF of the game that may hold textures, as a path from the game folder: the game's audio holds none.</summary>
    internal static IReadOnlyList<string> Archives(string root)
    {
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return [.. Directory.EnumerateFiles(root, "*.rpf", enumeration)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith("x64/audio/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private Names Read()
    {
        // Where it was when the read started, should the folder be moved meanwhile.
        string cachePath = CachePath, cacheFolder = Path.GetDirectoryName(cachePath)!;
        Dictionary<string, Part> cached = [];
        try
        {
            cached = ReadCache(cachePath, Version);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
        }
        var parts = new ConcurrentBag<Part>();
        var fresh = new ConcurrentBag<Part>();
        IReadOnlyList<string> archives = Archives(root);
        var stale = new List<(string Path, long Length, long Written)>();
        foreach (string archive in archives)
        {
            FileInfo info = new(Path.Combine(root, archive));
            if (!info.Exists)
                continue;
            if (cached.TryGetValue(archive.ToUpperInvariant(), out Part? part) && part.Length == info.Length && part.Written == info.LastWriteTimeUtc.Ticks)
                parts.Add(part);
            else
                stale.Add((archive, info.Length, info.LastWriteTimeUtc.Ticks));
        }
        // The biggest first, so the last to finish isn't a big one started late.
        Parallel.ForEach(stale.OrderByDescending(archive => archive.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, archive =>
        {
            Part part = Scanner(Path.Combine(root, archive.Path), archive.Path, archive.Length, archive.Written, target, crypto);
            parts.Add(part);
            fresh.Add(part);
        });
        Part[] all = [.. parts.OrderBy(part => part.Archive, StringComparer.OrdinalIgnoreCase)];
        if (!fresh.IsEmpty || cached.Count != all.Length)
        {
            try
            {
                Directory.CreateDirectory(cacheFolder);
                // An RPF that couldn't be read isn't kept, so the next run reads it again.
                WriteCache(cachePath, Version, [.. all.Where(part => !part.Failed)]);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return new Names(all);
    }

    /// <summary>What a cache must be made with to be read: the format, the edition, and the FiveFury that reads the dictionaries.</summary>
    private string Version => $"{FormatVersion}|{target}|{GameTextures.MadeWith}";

    /// <summary>
    /// Every dictionary in the RPF at <paramref name="file"/> (<paramref name="archive"/> from the game folder) with its
    /// textures' names; an RPF, nested one or dictionary that can't be read is left out.
    /// </summary>
    internal static Part Scan(string file, string archive, long length, long written, GameTarget target, GameCrypto? crypto)
    {
        var dictionaries = new List<Held>();
        bool failed = false;
        try
        {
            using RpfReadSession session = RpfReadSession.Open(file, crypto);
            Walk(session, session.Archive.Root, "");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or NotSupportedException or EndOfStreamException or ArgumentException)
        {
            failed = true;
        }
        return new Part(archive, length, written, [.. dictionaries], failed);

        void Walk(RpfReadSession session, RpfDirectoryEntry folder, string prefix)
        {
            foreach (RpfEntry entry in folder.Children)
            {
                string path = prefix + entry.Name;
                try
                {
                    if (entry is RpfDirectoryEntry child)
                        Walk(session, child, path + "/");
                    else if (entry is RpfBinaryFileEntry nested && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                        Walk(session, session.OpenNested(nested).Root, path + "/");
                    else if (entry is RpfResourceFileEntry resource && entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                        dictionaries.Add(new Held(path, resource.LogicalSize, NamesOf(resource, target)));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException
                    or EndOfStreamException or ArgumentException or OverflowException)
                {
                }
            }
        }
    }

    /// <summary>The name hashes of a dictionary's textures, read from its start, or the whole of it when they're further in.</summary>
    internal static uint[] NamesOf(RpfResourceFileEntry resource, GameTarget target)
    {
        ResourceTextureCatalog catalog;
        long stored = resource.StoredSize;
        // A resource stored compressed with XCompression, or after a prefix of another size, is read as FiveFury reads it.
        if (resource.XCompressed || stored <= FirstRead)
            catalog = ResourceTextureCatalog.Read(resource.ReadStandalone(), GameFileType.Ytd, target);
        else
        {
            byte[] start = new byte[FirstRead];
            using (Stream stream = resource.OpenStored())
                stream.ReadExactly(start);
            resource.Header.Write(start);
            try
            {
                catalog = ResourceTextureCatalog.Read(start, GameFileType.Ytd, target);
            }
            catch (InvalidDataException)
            {
                catalog = ResourceTextureCatalog.Read(resource.ReadStandalone(), GameFileType.Ytd, target);
            }
        }
        return [.. catalog.Dictionaries.SelectMany(dictionary => dictionary.Textures)
            .Where(texture => texture.Name is not null).Select(texture => NameHash(texture.Name!)).Distinct()];
    }

    /// <summary>How a texture's name is kept: its hash, whatever its case.</summary>
    internal static uint NameHash(string name) => new Hash32(name.ToLowerInvariant()).Value;

    internal static void WriteCache(string path, string version, IReadOnlyList<Part> parts)
    {
        string temporary = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
        {
            writer.Write("HHTN"u8);
            writer.Write(version);
            writer.Write(parts.Count);
            foreach (Part part in parts)
            {
                writer.Write(part.Archive);
                writer.Write(part.Length);
                writer.Write(part.Written);
                writer.Write(part.Dictionaries.Length);
                foreach (Held held in part.Dictionaries)
                {
                    writer.Write(held.Path);
                    writer.Write(held.Size);
                    writer.Write(held.Names.Length);
                    foreach (uint name in held.Names)
                        writer.Write(name);
                }
            }
        }
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The cached parts by their RPF's path, upper case; none when there's no cache, or it's of another version.</summary>
    internal static Dictionary<string, Part> ReadCache(string path, string version)
    {
        var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return parts;
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("HHTN"u8) || reader.ReadString() != version)
            return parts;
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            string archive = reader.ReadString();
            long length = reader.ReadInt64(), written = reader.ReadInt64();
            int count = reader.ReadInt32();
            if (count < 0 || count > 10_000_000)
                throw new InvalidDataException("Corrupt texture directory.");
            var dictionaries = new Held[count];
            for (int j = 0; j < count; j++)
            {
                string held = reader.ReadString();
                long size = reader.ReadInt64();
                int names = reader.ReadInt32();
                if (names < 0 || names > 1_000_000)
                    throw new InvalidDataException("Corrupt texture directory.");
                var hashes = new uint[names];
                for (int k = 0; k < names; k++)
                    hashes[k] = reader.ReadUInt32();
                dictionaries[j] = new Held(held, size, hashes);
            }
            parts[archive.ToUpperInvariant()] = new Part(archive, length, written, dictionaries);
        }
        return parts;
    }

    /// <summary>A dictionary in an RPF: its path in it, nested RPFs as folders, its size, and its textures' name hashes.</summary>
    internal sealed record Held(string Path, long Size, uint[] Names);

    /// <summary>What an RPF holds (<paramref name="Archive"/>, from the game folder) as it was at that size and time written.</summary>
    internal sealed record Part(string Archive, long Length, long Written, Held[] Dictionaries, bool Failed = false);

    /// <summary>The game's dictionaries by the names they hold.</summary>
    internal sealed class Names
    {
        private readonly GameDictionary[] dictionaries;
        private readonly Dictionary<uint, int[]> byName;

        public Names(IReadOnlyList<Part> parts)
        {
            Complete = parts.All(part => !part.Failed);
            var all = new List<GameDictionary>();
            var lists = new Dictionary<uint, List<int>>();
            foreach (Part part in parts)
                foreach (Held held in part.Dictionaries)
                {
                    int index = all.Count;
                    all.Add(new GameDictionary(part.Archive + "/" + held.Path, held.Size));
                    foreach (uint name in held.Names)
                    {
                        if (!lists.TryGetValue(name, out List<int>? list))
                            lists[name] = list = [];
                        list.Add(index);
                    }
                }
            dictionaries = [.. all];
            byName = lists.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        /// <summary>Whether every RPF was read: one that couldn't be may hold what none of the rest does.</summary>
        public bool Complete { get; }

        /// <summary>How many dictionaries there are.</summary>
        public int Count => dictionaries.Length;

        public IReadOnlyList<GameDictionary> Holding(string textureName, string? nearPath)
        {
            if (!byName.TryGetValue(NameHash(textureName), out int[]? found))
                return [];
            string[] near = nearPath is null ? [] : nearPath.Split('/')[..^1];
            return [.. found.Select(index => dictionaries[index])
                .OrderByDescending(dictionary => Common(dictionary.Path, near))
                .ThenBy(dictionary => Rank(dictionary.Path))
                .ThenBy(dictionary => dictionary.Path, StringComparer.OrdinalIgnoreCase)];
        }

        // How many folders a dictionary's path shares with the model's, from the game folder down.
        private static int Common(string path, string[] near)
        {
            string[] parts = path.Split('/');
            int count = 0;
            while (count < near.Length && count < parts.Length - 1 && string.Equals(parts[count], near[count], StringComparison.OrdinalIgnoreCase))
                count++;
            return count;
        }

        // Which copy the game takes when several are as near: a mod's, then the game's update, then its own.
        private static int Rank(string path) =>
            path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ? 0
            : path.StartsWith("update/", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;
    }
}
