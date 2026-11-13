using System.Collections.Concurrent;
using System.IO.Compression;
using FiveFury.Cryptography;
using FiveFury.Rpf;

namespace HikariHopper.Core.Search;

/// <summary>One indexed file: the folder it is in (an index into its group's folders) and its name and size.</summary>
public readonly record struct IndexedEntry(int Folder, string Name, long Size);

/// <summary>
/// A group of indexed files that share a stamp: the loose files of the game, or everything inside one RPF on disk
/// (nested RPFs included). Folders are paths from the game folder, with RPFs as folders and <c>/</c> between parts.
/// </summary>
public sealed record IndexedGroup(string ArchivePath, long Length, long WriteTicks, string[] Folders, IndexedEntry[] Files)
{
    public string PathOf(IndexedEntry entry)
    {
        string folder = Folders[entry.Folder];
        return folder.Length == 0 ? entry.Name : $"{folder}/{entry.Name}";
    }
}

/// <summary>A file found by a search: its path from the game folder, its size, and the parts of its name that matched.</summary>
public sealed record SearchHit(string Path, long Size, IReadOnlyList<(int Start, int Length)> Spans)
{
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>The folders before the name, RPFs included; empty for a file at the root.</summary>
    public string Location => Path.LastIndexOf('/') is var slash and >= 0 ? Path[..slash] : "";
}

/// <summary>What the index holds at one moment, and how far its refresh has got.</summary>
public sealed record SearchIndexState(IReadOnlyList<IndexedGroup> Groups, int ArchivesDone, int ArchivesTotal, bool Complete)
{
    public static readonly SearchIndexState Empty = new([], 0, 0, false);

    /// <summary>
    /// The files whose names match <paramref name="pattern"/>, under <paramref name="scope"/> (a folder path from
    /// the game folder; empty for everything), at most <paramref name="limit"/>.
    /// </summary>
    public List<SearchHit> Search(SearchPattern pattern, string scope, int limit, CancellationToken cancel = default)
    {
        var hits = new List<SearchHit>();
        string prefix = scope.Length == 0 ? "" : scope.TrimEnd('/') + "/";
        foreach (IndexedGroup group in Groups)
        {
            cancel.ThrowIfCancellationRequested();
            // Each folder's scope is decided once, not once per file in it.
            bool[] inScope = [.. group.Folders.Select(folder => prefix.Length == 0
                || (folder + "/").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
            if (!inScope.Contains(true))
                continue;
            for (int index = 0; index < group.Files.Length; index++)
            {
                // A superseded search stops soon, even inside one large group and with a slow regular expression.
                if ((index & 255) == 0)
                    cancel.ThrowIfCancellationRequested();
                IndexedEntry entry = group.Files[index];
                if (!inScope[entry.Folder] || pattern.Match(entry.Name) is not { } spans)
                    continue;
                hits.Add(new SearchHit(group.PathOf(entry), entry.Size, spans));
                if (hits.Count >= limit)
                    return hits;
            }
        }
        return hits;
    }
}

/// <summary>
/// Every file of a game, inside its RPFs too, by name, for searching it all at once. Built in the background the
/// first time, from each RPF's table of contents only, then kept in a cache on disk: a refresh lists the loose files
/// again and reads only the RPFs whose size or time changed. One index is shared by every tab on the same game.
/// </summary>
public sealed class SearchIndex
{
    private const int CacheVersion = 1;
    private const string LooseGroup = "";
    private static readonly ConcurrentDictionary<string, SearchIndex> Shared = new(StringComparer.OrdinalIgnoreCase);

    private readonly object gate = new();
    private readonly GameCrypto? crypto;
    private Task running = Task.CompletedTask;
    private bool refreshing;
    private bool again;
    private bool cacheRead;
    // Set by a rebuild: the next refresh forgets what it knows and reads every RPF again.
    private bool startOver;
    // Set once its file on disk is deleted, or when writing it failed: the next refresh writes it again, changed or not.
    private volatile bool cacheGone;
    private volatile SearchIndexState state = SearchIndexState.Empty;

    private SearchIndex(string root, bool standaloneArchive, GameCrypto? crypto)
    {
        Root = root;
        StandaloneArchive = standaloneArchive;
        this.crypto = crypto;
    }

    /// <summary>Where the indexes are cached; tests point it elsewhere.</summary>
    public static string CacheFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HikariHopper", "Index");

    /// <summary>The game folder, or the RPF file when an archive is searched on its own.</summary>
    public string Root { get; }

    /// <summary>Whether <see cref="Root"/> is an RPF opened outside any game: its paths start with its file name.</summary>
    public bool StandaloneArchive { get; }

    public SearchIndexState State => state;

    /// <summary>How many RPFs refreshes have read from disk, the cache aside; for tests.</summary>
    internal int ArchivesRead { get; private set; }

    /// <summary>Raised, off the UI thread, whenever <see cref="State"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The index of the game at <paramref name="gameRoot"/>, shared by every caller.</summary>
    public static SearchIndex ForGame(string gameRoot, GameCrypto? crypto) =>
        Shared.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot)), root => new SearchIndex(root, false, crypto));

    /// <summary>An index of one RPF opened outside a game.</summary>
    public static SearchIndex ForArchive(string archivePath, GameCrypto? crypto) =>
        Shared.GetOrAdd(Path.GetFullPath(archivePath), root => new SearchIndex(root, true, crypto));

    /// <summary>Forgets every shared index; for tests.</summary>
    internal static void ForgetAll() => Shared.Clear();

    /// <summary>
    /// Deletes every index on disk. Those in use keep what they hold, and write theirs again on their next refresh;
    /// the others are read from their game or RPF the next time it's searched. False when a file couldn't go.
    /// </summary>
    public static bool DeleteCache()
    {
        bool all = CacheFiles.Delete(CacheFolder, "*.index") & CacheFiles.Delete(CacheFolder, "*.tmp");
        foreach (SearchIndex index in Shared.Values)
            index.cacheGone = true;
        return all;
    }

    /// <summary>
    /// Deletes every index on disk and reads again, from scratch, each game and RPF searched since the app started.
    /// Done once they are complete.
    /// </summary>
    public static Task RebuildAll()
    {
        DeleteCache();
        return Task.WhenAll(Shared.Values.Select(index => index.StartOver()));
    }

    private Task StartOver()
    {
        lock (gate)
            startOver = true;
        return Refresh();
    }

    /// <summary>
    /// Brings the index up to date: starts a refresh, or, while one runs, has it run once more when it ends so
    /// nothing changed meanwhile is missed. The task ends when the index is complete.
    /// </summary>
    public Task Refresh()
    {
        lock (gate)
        {
            if (refreshing)
            {
                again = true;
                return running;
            }
            refreshing = true;
            again = false;
            running = Task.Run(RunRefreshes);
            return running;
        }
    }

    private void RunRefreshes()
    {
        // Whether this run handed the flag back itself; only a run that didn't (it threw) clears it on the way out,
        // or it could clear the flag of a newer run started meanwhile.
        bool released = false;
        try
        {
            while (true)
            {
                try
                {
                    RefreshOnce();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // The game folder went away (a drive disconnected, say): what was known stays, and the index is
                    // done rather than indexing for ever.
                    SearchIndexState last = state;
                    Publish(last with { ArchivesDone = last.ArchivesTotal, Complete = true });
                }
                lock (gate)
                {
                    if (!again)
                    {
                        refreshing = false;
                        released = true;
                        return;
                    }
                    again = false;
                }
            }
        }
        finally
        {
            if (!released)
            {
                lock (gate)
                    refreshing = false;
            }
        }
    }

    private void Publish(SearchIndexState next)
    {
        state = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshOnce()
    {
        bool fresh;
        lock (gate)
        {
            fresh = startOver;
            startOver = false;
        }
        // Rebuilt, it knows nothing and reads no cache: every RPF is read again.
        Dictionary<string, IndexedGroup> known = fresh
            ? new(StringComparer.OrdinalIgnoreCase)
            : state.Groups.ToDictionary(group => group.ArchivePath, StringComparer.OrdinalIgnoreCase);
        if (fresh)
            cacheRead = true;
        if (!cacheRead)
        {
            cacheRead = true;
            foreach (IndexedGroup group in ReadCache())
                known.TryAdd(group.ArchivePath, group);
        }

        // What is on disk now: the loose files, and each RPF with its stamp.
        var archives = new List<(string Path, string Full, long Length, long Ticks)>();
        IndexedGroup? loose = null;
        if (StandaloneArchive)
        {
            var info = new FileInfo(Root);
            if (info.Exists)
                archives.Add((info.Name, info.FullName, info.Length, info.LastWriteTimeUtc.Ticks));
        }
        else
        {
            loose = ListLoose(archives);
        }

        var groups = new List<IndexedGroup>();
        if (loose is not null)
            groups.Add(loose);
        var stale = new List<(string Path, string Full, long Length, long Ticks)>();
        foreach (var archive in archives)
        {
            if (known.TryGetValue(archive.Path, out IndexedGroup? cached) && cached.Length == archive.Length && cached.WriteTicks == archive.Ticks)
                groups.Add(cached);
            else
                stale.Add(archive);
        }
        int done = archives.Count - stale.Count;
        Publish(new SearchIndexState([.. groups], done, archives.Count, stale.Count == 0));

        long lastPublish = Environment.TickCount64;
        foreach (var archive in stale)
        {
            if (ReadArchive(archive.Path, archive.Full, archive.Length, archive.Ticks) is { } group)
                groups.Add(group);
            done++;
            // Results come as the archives are read, a few times a second at most.
            if (Environment.TickCount64 - lastPublish > 250 || done == archives.Count)
            {
                Publish(new SearchIndexState([.. groups], done, archives.Count, done == archives.Count));
                lastPublish = Environment.TickCount64;
            }
        }
        if (stale.Count > 0 || !state.Complete || cacheGone)
        {
            cacheGone = false;
            Publish(new SearchIndexState([.. groups], archives.Count, archives.Count, true));
            // Cleared first, so a deletion while it is written still counts.
            if (!WriteCache(groups.Where(group => group.ArchivePath != LooseGroup)))
                cacheGone = true;
        }
    }

    /// <summary>The game's loose files as one group; every RPF among them goes to <paramref name="archives"/>.</summary>
    private IndexedGroup ListLoose(List<(string Path, string Full, long Length, long Ticks)> archives)
    {
        var folders = new List<string>();
        var folderIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new List<IndexedEntry>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };
        foreach (FileInfo file in new DirectoryInfo(Root).EnumerateFiles("*", options))
        {
            string relative = Path.GetRelativePath(Root, file.FullName).Replace('\\', '/');
            int slash = relative.LastIndexOf('/');
            string folder = slash < 0 ? "" : relative[..slash];
            if (!folderIndex.TryGetValue(folder, out int index))
            {
                index = folders.Count;
                folders.Add(folder);
                folderIndex[folder] = index;
            }
            files.Add(new IndexedEntry(index, file.Name, file.Length));
            if (file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                archives.Add((relative, file.FullName, file.Length, file.LastWriteTimeUtc.Ticks));
        }
        return new IndexedGroup(LooseGroup, 0, 0, [.. folders], [.. files]);
    }

    /// <summary>Everything inside one RPF on disk, its nested RPFs included; null when it can't be read.</summary>
    private IndexedGroup? ReadArchive(string path, string full, long length, long ticks)
    {
        var folders = new List<string>();
        var folderIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new List<IndexedEntry>();
        void Walk(ArchiveNode node, string prefix)
        {
            foreach (RpfEntry entry in node.Archive.EnumerateEntries())
            {
                if (entry is not RpfFileEntry file)
                    continue;
                string inner = entry.Path;
                int slash = inner.LastIndexOf('/');
                string folder = slash < 0 ? prefix : $"{prefix}/{inner[..slash]}";
                if (!folderIndex.TryGetValue(folder, out int index))
                {
                    index = folders.Count;
                    folders.Add(folder);
                    folderIndex[folder] = index;
                }
                files.Add(new IndexedEntry(index, entry.Name, file.LogicalSize > 0 ? file.LogicalSize : file.StoredSize));
                if (entry is RpfBinaryFileEntry binary && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Walk(node.OpenNested(binary), $"{prefix}/{inner}");
                    }
                    catch (InvalidDataException)
                    {
                        // A nested RPF that doesn't decode is still listed as a file; its content just isn't.
                    }
                }
            }
        }
        ArchiveNode? root = null;
        ArchivesRead++;
        try
        {
            root = ArchiveNode.OpenRoot(full, crypto ?? GameKeys.TryLoadDefault());
            Walk(root, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or NotSupportedException or ArgumentException or InvalidOperationException or EndOfStreamException)
        {
            return null;
        }
        finally
        {
            root?.Close();
        }
        return new IndexedGroup(path, length, ticks, [.. folders], [.. files]);
    }

    private string CachePath
    {
        get
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Root.ToUpperInvariant()));
            return Path.Combine(CacheFolder, Convert.ToHexString(hash, 0, 12) + ".index");
        }
    }

    private List<IndexedGroup> ReadCache()
    {
        var groups = new List<IndexedGroup>();
        try
        {
            using var file = File.OpenRead(CachePath);
            using var unzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(unzip, System.Text.Encoding.UTF8);
            if (reader.ReadInt32() != CacheVersion || !string.Equals(reader.ReadString(), Root, StringComparison.OrdinalIgnoreCase))
                return [];
            int count = reader.ReadInt32();
            for (int g = 0; g < count; g++)
            {
                string path = reader.ReadString();
                long length = reader.ReadInt64(), ticks = reader.ReadInt64();
                var folders = new string[reader.ReadInt32()];
                for (int f = 0; f < folders.Length; f++)
                    folders[f] = reader.ReadString();
                var files = new IndexedEntry[reader.ReadInt32()];
                for (int e = 0; e < files.Length; e++)
                    files[e] = new IndexedEntry(reader.Read7BitEncodedInt(), reader.ReadString(), reader.Read7BitEncodedInt64());
                groups.Add(new IndexedGroup(path, length, ticks, folders, files));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException
            or FormatException or IndexOutOfRangeException)
        {
            return [];
        }
        return groups;
    }

    // Whether the file was written.
    private bool WriteCache(IEnumerable<IndexedGroup> groups)
    {
        string target = CachePath;
        string temporary = target + ".tmp";
        try
        {
            Directory.CreateDirectory(CacheFolder);
            IndexedGroup[] all = [.. groups];
            using (var file = File.Create(temporary))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(zip, System.Text.Encoding.UTF8))
            {
                writer.Write(CacheVersion);
                writer.Write(Root);
                writer.Write(all.Length);
                foreach (IndexedGroup group in all)
                {
                    writer.Write(group.ArchivePath);
                    writer.Write(group.Length);
                    writer.Write(group.WriteTicks);
                    writer.Write(group.Folders.Length);
                    foreach (string folder in group.Folders)
                        writer.Write(folder);
                    writer.Write(group.Files.Length);
                    foreach (IndexedEntry entry in group.Files)
                    {
                        writer.Write7BitEncodedInt(entry.Folder);
                        writer.Write(entry.Name);
                        writer.Write7BitEncodedInt64(entry.Size);
                    }
                }
            }
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Only a cache: the next refresh tries again, and the next session reads the archives again meanwhile.
            try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }
}
