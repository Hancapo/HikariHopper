using FiveFury;
using FiveFury.Assets;
using FiveFury.Cryptography;
using FiveFury.Rpf;

namespace HikariHopper.Core;

/// <summary>Owns the selected game root and lazily opened FiveFury archives.</summary>
public sealed class RpfProvider
{
    private static readonly Dictionary<GameFileType, string> GameFileKindLabels = new()
    {
        [GameFileType.Ydd] = "Drawable Dictionary",
        [GameFileType.Ydr] = "Drawable",
        [GameFileType.Yft] = "Fragment",
        [GameFileType.Ymap] = "Map Data",
        [GameFileType.Ymf] = "Map Manifest",
        [GameFileType.Ymt] = "Metadata Container",
        [GameFileType.Ytd] = "Texture Dictionary",
        [GameFileType.Ytyp] = "Archetype Definitions",
        [GameFileType.Ybn] = "Collision Bounds",
        [GameFileType.Ycd] = "Clip Dictionary",
        [GameFileType.Yed] = "Expression Dictionary",
        [GameFileType.Ypt] = "Particle Dictionary",
        [GameFileType.Ynd] = "Node Dictionary",
        [GameFileType.Ynv] = "Navigation Mesh",
        [GameFileType.Rel] = "Audio Metadata",
        [GameFileType.Ywr] = "Waypoint Recording",
        [GameFileType.Yvr] = "Vehicle Recording",
        [GameFileType.Gxt2] = "Text Table",
        [GameFileType.Gtxd] = "Texture Parent Data",
        [GameFileType.Awc] = "Audio Container",
        [GameFileType.Vehicles] = "Vehicle Definitions",
        [GameFileType.CarCols] = "Vehicle Colors",
        [GameFileType.CarModCols] = "Vehicle Mod Colors",
        [GameFileType.CarVariations] = "Vehicle Variations",
        [GameFileType.VehicleLayouts] = "Vehicle Layouts",
        [GameFileType.Peds] = "Ped Definitions",
        [GameFileType.Heightmap] = "Height Map",
        [GameFileType.Water] = "Water Data",
        [GameFileType.Cut] = "Cutscene",
        [GameFileType.Cdr] = "PS3 Drawable",
        [GameFileType.Handling] = "Vehicle Handling",
        [GameFileType.Gta5Cache] = "GTA V Cache",
        [GameFileType.ExpressionSets] = "Expression Sets",
        [GameFileType.Rpf] = "RPF Package",
    };

    private static readonly Dictionary<string, string> GeneralFileKindLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "Application",
        [".dll"] = "Application Extension",
        [".asi"] = "ASI Plugin",
        [".meta"] = "Metadata",
        [".xml"] = "XML Document",
        [".json"] = "JSON Document",
        [".txt"] = "Text Document",
        [".dat"] = "Data File",
    };

    private string? gameRoot;
    private GameTarget? gameTarget;
    private ArchiveNode? archive;
    private string archivePath = "";
    private string archiveGamePath = "";
    private bool archiveIsNested;
    private bool inArchive;
    private GameCrypto? crypto;

    public WorkspaceInfo Info
    {
        get
        {
            if (InArchive)
                return new WorkspaceInfo(archive!.Name, archivePath, "RPF7", archive.Archive.Platform.ToString().ToUpperInvariant());
            if (gameRoot is not null)
                return new WorkspaceInfo(Path.GetFileName(gameRoot), gameRoot, GameEdition, "PC");
            return new WorkspaceInfo("No game", "", "—", "PC");
        }
    }

    /// <summary>"GTA V Legacy" or "GTA V Enhanced" while a game is open, otherwise "".</summary>
    public string GameEdition => gameRoot is null ? ""
        : gameTarget == FiveFury.GameTarget.Enhanced ? "GTA V Enhanced" : "GTA V Legacy";

    public bool IsOpen => gameRoot is not null || archive is not null;

    public bool HasGame => gameRoot is not null;

    public bool HasArchive => archive is not null;

    public bool InArchive => inArchive && archive is not null;

    public GameTarget? GameTarget => gameTarget;

    public GameCrypto? Crypto => crypto;

    public string GameName => gameRoot is not null ? Path.GetFileName(gameRoot) : "No game";

    public string GamePath => gameRoot ?? "";

    public string ArchiveName => archive?.Name ?? "";

    /// <summary>Path of the root RPF relative to the game folder, empty when outside it.</summary>
    public string ArchiveGamePath => archiveGamePath;

    public bool ArchiveIsNested => archiveIsNested;

    /// <summary>Full path of the current nested archive from the root archive; empty at the root.</summary>
    public string ArchivePrefix => archive?.Prefix ?? "";

    public string ArchiveTreeId
    {
        get
        {
            ArchiveNode? root = RootArchive();
            if (root is null)
                return "";
            return archiveGamePath.Length > 0 ? archiveGamePath : root.SourcePath ?? root.Name;
        }
    }

    public string RootArchiveName => RootArchive()?.Name ?? "";

    public IReadOnlyList<string> ArchiveChainPrefixes
    {
        get
        {
            var chain = new List<string>();
            for (ArchiveNode? node = archive; node is not null; node = node.Parent)
                chain.Add(node.Prefix);
            chain.Reverse();
            return chain;
        }
    }

    public static LoadedGame LoadGame(string path)
    {
        string root = Path.GetFullPath(path);
        if (!Directory.Exists(root))
            throw new ArgumentException($"Game folder does not exist: {root}");
        string enhanced = Path.Combine(root, "GTA5_Enhanced.exe");
        string legacy = Path.Combine(root, "GTA5.exe");
        (GameTarget target, string executable) = File.Exists(enhanced)
            ? (FiveFury.GameTarget.Enhanced, enhanced)
            : File.Exists(legacy)
                ? (FiveFury.GameTarget.Legacy, legacy)
                : throw new ArgumentException("Select the GTA V folder containing GTA5.exe or GTA5_Enhanced.exe");
        return new LoadedGame(root.TrimEnd(Path.DirectorySeparatorChar), target, GameKeys.Load(executable));
    }

    public WorkspaceInfo OpenGame(string path) => ApplyLoadedGame(LoadGame(path));

    public WorkspaceInfo ApplyLoadedGame(LoadedGame loaded)
    {
        Close();
        gameRoot = loaded.Root;
        gameTarget = loaded.Target;
        crypto = loaded.Crypto;
        inArchive = false;
        return Info;
    }

    public WorkspaceInfo OpenArchive(string path)
    {
        string resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new ArgumentException($"Archive does not exist: {resolved}");
        if (!string.Equals(Path.GetExtension(resolved), ".rpf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("HikariHopper can only open .rpf archives");
        ArchiveNode opened = ArchiveNode.OpenRoot(resolved, crypto ?? GameKeys.TryLoadDefault());
        CloseArchiveCore();
        archive = opened;
        archivePath = resolved;
        archiveGamePath = RelativeGamePath(resolved);
        archiveIsNested = false;
        inArchive = true;
        return Info;
    }

    public WorkspaceInfo ShowGame()
    {
        if (gameRoot is null)
            throw new ArgumentException("No GTA V installation is loaded");
        inArchive = false;
        return Info;
    }

    /// <summary>Closes the open archive when it lives at or below one of <paramref name="paths"/>.</summary>
    public bool ReleaseOpenArchiveFor(IEnumerable<string> paths)
    {
        ArchiveNode? root = RootArchive();
        if (root?.SourcePath is null)
            return false;
        string source = Path.GetFullPath(root.SourcePath);
        bool affected = paths.Select(Path.GetFullPath).Any(path =>
            string.Equals(source, path, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (!affected)
            return false;
        CloseArchiveCore();
        inArchive = false;
        return true;
    }

    public WorkspaceInfo ShowArchive()
    {
        if (archive is null)
            throw new ArgumentException("No RPF archive is open");
        inArchive = true;
        return Info;
    }

    public WorkspaceInfo ShowRootArchive()
    {
        ArchiveNode root = RootArchive() ?? throw new ArgumentException("No RPF archive is open");
        archive = root;
        archivePath = root.SourcePath ?? root.Name;
        archiveIsNested = false;
        inArchive = true;
        return Info;
    }

    public void CloseArchive()
    {
        CloseArchiveCore();
        inArchive = false;
    }

    public void Close()
    {
        CloseArchiveCore();
        gameRoot = null;
        gameTarget = null;
        crypto = null;
        inArchive = false;
    }

    public List<EntryRecord> Entries(string directoryPath = ".") =>
        InArchive ? ArchiveEntries(directoryPath) : GameEntries(directoryPath);

    public bool HasDirectory(string path)
    {
        if (InArchive)
            return archive!.DirectoryAt(path) is not null;
        return gameRoot is not null && Directory.Exists(Path.Combine(gameRoot, path));
    }

    public EntryCreationTarget CreationTarget(string directoryPath = ".")
    {
        string normalized = EntryNames.NormalizedDirectory(directoryPath);
        if (InArchive)
        {
            if (archive!.DirectoryAt(normalized) is null)
                throw new ArgumentException($"Archive folder does not exist: {normalized}");
            ArchiveNode root = RootArchive()!;
            string rootPath = Path.GetFullPath(root.SourcePath ?? "");
            if (!File.Exists(rootPath))
                throw new ArgumentException("The root RPF archive is no longer available");
            var target = new EntryCreationTarget(
                ArchivePath: rootPath,
                ArchivePrefix: ArchivePrefix,
                InternalDirectory: normalized,
                Game: gameTarget,
                Crypto: root.Crypto);
            root.Close();
            return target;
        }

        if (gameRoot is null)
            throw new ArgumentException("No writable explorer location is open");
        string rootDirectory = Path.GetFullPath(gameRoot);
        string directory = normalized == "." ? rootDirectory : Path.GetFullPath(Path.Combine(rootDirectory, normalized));
        if (!IsWithin(rootDirectory, directory))
            throw new ArgumentException($"Folder is outside the game installation: {normalized}");
        if (!Directory.Exists(directory))
            throw new ArgumentException($"Game folder does not exist: {normalized}");
        return new EntryCreationTarget(Directory: directory, Game: gameTarget, Crypto: crypto);
    }

    public EntryDeletionTarget DeletionTarget(IReadOnlyList<EntryRecord> entries, string directoryPath = ".")
    {
        if (entries.Count == 0)
            throw new ArgumentException("No items are selected");
        if (InArchive)
        {
            if (entries.Any(entry => !ReferenceEquals(entry.ArchiveItem?.Node, archive)))
                throw new ArgumentException("A selected item is outside the active RPF");
            if (entries.Any(entry => entry.ArchiveItem!.Entry.Parent is null))
                throw new ArgumentException("Cannot delete the archive root");
        }
        EntryCreationTarget location = CreationTarget(directoryPath);
        if (location.InArchive)
            return new EntryDeletionTarget(location, [], entries.Select(EntryLocalPath).ToArray());

        var loosePaths = entries.Select(GameEntryCandidate).ToArray();
        string root = Path.GetFullPath(gameRoot!);
        if (loosePaths.Any(path => string.Equals(path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Cannot delete the game root");
        string lexicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot!));
        foreach (EntryRecord entry in entries)
        {
            for (string? path = Path.GetFullPath(Path.Combine(gameRoot!, entry.Path)); path is not null;
                path = Path.GetDirectoryName(path))
            {
                if (string.Equals(Path.TrimEndingDirectorySeparator(path), lexicalRoot, StringComparison.OrdinalIgnoreCase))
                    break;
                if (EntryOperations.IsLinkedPath(path))
                    throw new ArgumentException("Deletion through linked files or folders is not supported");
            }
        }
        ReleaseOpenArchiveFor(loosePaths);
        return new EntryDeletionTarget(location, loosePaths, []);
    }

    /// <summary>Materializes entries for a native file drag, using standalone RPF bytes.</summary>
    public List<string> ExportEntries(IReadOnlyList<EntryRecord> entries, string outputDirectory)
    {
        if (!InArchive)
            return entries.Select(GameEntryPath).ToList();
        string root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var exported = new List<string>();
        foreach (EntryRecord entry in entries)
        {
            string destination = SafeExportChild(root, entry.Name);
            ExportArchiveEntry(entry, destination);
            exported.Add(destination);
        }
        return exported;
    }

    /// <summary>Reads one loose or archived file as a standalone byte stream.</summary>
    public byte[] ReadEntryBytes(EntryRecord entry)
    {
        if (entry.IsDirectory)
            throw new ArgumentException($"Cannot read a directory as a file: {entry.Name}");
        if (InArchive)
        {
            if (entry.ArchiveItem?.Entry is not RpfFileEntry file)
                throw new ArgumentException($"Archive entry is unavailable: {entry.Name}");
            return file.ReadStandalone();
        }
        return File.ReadAllBytes(GameEntryPath(entry));
    }

    /// <summary>The stable source notation shown by secondary viewers.</summary>
    public string EntrySourcePath(EntryRecord entry)
    {
        if (InArchive)
            return $"{(archiveGamePath.Length > 0 ? archiveGamePath : archivePath)}::{entry.Path}";
        return GameEntryPath(entry);
    }

    public ArchiveEntryTarget GetArchiveEntryTarget(EntryRecord entry)
    {
        if (!InArchive || entry.ArchiveItem is not { } item)
            throw new ArgumentException("The selected entry is not inside an RPF archive");
        if (item.Entry.Archive is null)
            throw new ArgumentException("The selected archive entry is detached");
        ArchiveNode root = item.Node.Root;
        string rootPath = Path.GetFullPath(root.SourcePath ?? "");
        if (!File.Exists(rootPath))
            throw new ArgumentException("The root RPF archive is no longer available");
        return new ArchiveEntryTarget(rootPath, item.FullPath, root.Crypto, root);
    }

    /// <summary>Reopens the archive chain after <paramref name="rootPath"/> was rewritten, keeping navigation.</summary>
    public bool ReloadArchive(string rootPath)
    {
        if (archive is null)
            return false;
        ArchiveNode root = archive.Root;
        string expected = Path.GetFullPath(root.SourcePath ?? "");
        if (!string.Equals(expected, Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
            return false;

        string currentPrefix = archive.Prefix;
        bool wasInArchive = inArchive;
        root.Close();
        ArchiveNode reloadedRoot = ArchiveNode.OpenRoot(expected, crypto ?? root.Crypto);
        ArchiveNode reloaded = reloadedRoot;
        if (currentPrefix.Length > 0)
        {
            try
            { reloaded = ArchiveWriter.NestedArchive(reloadedRoot, currentPrefix); }
            catch (ArgumentException)
            {
                // A peer may have deleted the nested archive we were browsing.
                reloaded = reloadedRoot;
            }
            catch
            {
                reloadedRoot.Close();
                throw;
            }
        }
        archive = reloaded;
        archivePath = reloaded.SourcePath ?? reloaded.Name;
        archiveIsNested = reloaded.Parent is not null;
        inArchive = wasInArchive;
        return true;
    }

    /// <summary>Releases the current handle while retaining navigation for a reload.</summary>
    public void PrepareArchiveWrite(string rootPath)
    {
        ArchiveNode? root = RootArchive();
        if (root?.SourcePath is not null
            && string.Equals(Path.GetFullPath(root.SourcePath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
            root.Close();
    }

    public WorkspaceInfo OpenNested(EntryRecord entry)
    {
        if (archive is null || entry.ArchiveItem is not { Entry: RpfBinaryFileEntry binary } item)
            throw new ArgumentException("The selected entry does not contain a nested archive");
        if (!ReferenceEquals(item.Node.Root, archive.Root))
            throw new ArgumentException("The selected entry does not belong to the open RPF");
        ArchiveNode nested;
        try
        { nested = item.Node.OpenNested(binary); }
        catch (InvalidDataException)
        { throw new ArgumentException($"FiveFury could not decode {entry.Name} as a nested RPF archive"); }
        archive = nested;
        archivePath = entry.Path;
        archiveIsNested = true;
        inArchive = true;
        return Info;
    }

    public EntryRecord? ArchiveEntry(string path)
    {
        string normalized = EntryNames.NormalizedDirectory(path);
        int slash = normalized.LastIndexOf('/');
        string parent = slash < 0 ? "." : normalized[..slash];
        return ArchiveEntries(parent).FirstOrDefault(entry => entry.Path.Replace('\\', '/') == normalized);
    }

    public EntryRecord? ArchiveEntryFromRoot(string path)
    {
        ArchiveNode? root = RootArchive();
        if (root is null)
            return null;
        return root.FindEntry(path) is { } found ? ArchiveRecord(found.Node, found.Entry) : null;
    }

    public List<EntryRecord> ArchiveEntriesAt(string archivePrefix, string directoryPath = ".")
    {
        ArchiveNode? node = ArchiveByPrefix(archivePrefix);
        return node is null ? [] : ArchiveRecords(node, directoryPath);
    }

    public bool ArchiveHasTreeChildren(string archivePrefix, string directoryPath = ".") =>
        ArchiveEntriesAt(archivePrefix, directoryPath).Any(entry =>
            entry.IsDirectory || entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));

    public WorkspaceInfo ActivateEntryArchive(EntryRecord entry)
    {
        ArchiveNode? node = entry.ArchiveItem?.Node;
        if (node is null || archive is null || !ReferenceEquals(node.Root, archive.Root))
            throw new ArgumentException("The selected entry does not belong to the open RPF");
        archive = node;
        archivePath = node.SourcePath ?? node.Name;
        archiveIsNested = node.Parent is not null;
        inArchive = true;
        return Info;
    }

    public static string EntryArchivePrefix(EntryRecord entry) => entry.ArchiveItem?.Node.Prefix ?? "";

    public static string EntryLocalPath(EntryRecord entry) => entry.ArchiveItem?.LocalPath ?? entry.Path;

    public static string LoadedNestedPrefix(EntryRecord entry) =>
        entry.ArchiveItem is { } item && item.Node.LoadedChild(item.Entry) is { } child ? child.Prefix : "";

    public List<EntryRecord> GameEntries(string directoryPath)
    {
        if (gameRoot is null)
            return [];
        string relative = EntryNames.NormalizedDirectory(directoryPath);
        string directory = relative == "." ? gameRoot : Path.Combine(gameRoot, relative);
        if (!Directory.Exists(directory))
            return [];
        List<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .OrderBy(item => item is not DirectoryInfo)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var records = new List<EntryRecord>();
        foreach (FileSystemInfo item in children)
        {
            try
            {
                string relativePath = Path.GetRelativePath(gameRoot, item.FullName).Replace('\\', '/');
                if (item is DirectoryInfo folder)
                {
                    var (total, navigable) = ChildCounts(folder);
                    records.Add(new EntryRecord(item.Name, relativePath, EntryRecord.FolderKind, 0, total, item, navigable));
                }
                else if (item is FileInfo file)
                {
                    records.Add(new EntryRecord(item.Name, relativePath, FileKind(item.Name), file.Length, 0, item));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        return records;
    }

    public List<EntryRecord> ArchiveEntries(string directoryPath) =>
        archive is null ? [] : ArchiveRecords(archive, directoryPath);

    public static string FileKind(string name)
    {
        if (GameFileKindLabels.TryGetValue(GameFileTypes.Classify(name), out string? label))
            return label;
        return GeneralFileKindLabels.GetValueOrDefault(Path.GetExtension(name), "File");
    }

    private static List<EntryRecord> ArchiveRecords(ArchiveNode node, string directoryPath)
    {
        // Closing only releases the file handle; the table stays listable, as in FiveFury Python.
        if (node.DirectoryAt(directoryPath) is not { } directory)
            return [];
        return directory.Children.OfType<RpfDirectoryEntry>().Cast<RpfEntry>()
            .Concat(directory.Children.OfType<RpfFileEntry>())
            .Select(item => ArchiveRecord(node, item))
            .ToList();
    }

    private static EntryRecord ArchiveRecord(ArchiveNode node, RpfEntry item)
    {
        var native = new ArchiveItem(node, item);
        if (item is RpfDirectoryEntry directory)
        {
            int folders = directory.Children.Count(child => child is RpfDirectoryEntry);
            int navigable = folders + directory.Children.Count(child =>
                child is RpfFileEntry && child.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
            return new EntryRecord(item.Name, native.FullPath, EntryRecord.FolderKind, 0,
                directory.Children.Count, native, navigable);
        }
        var file = (RpfFileEntry)item;
        long size = file.LogicalSize > 0 ? file.LogicalSize : file.StoredSize;
        return new EntryRecord(item.Name, native.FullPath, FileKind(item.Name), size, 0, native);
    }

    private void ExportArchiveEntry(EntryRecord entry, string destination)
    {
        if (entry.IsDirectory)
        {
            Directory.CreateDirectory(destination);
            if (entry.ArchiveItem is { } item)
                foreach (EntryRecord child in ArchiveRecords(item.Node, item.LocalPath))
                    ExportArchiveEntry(child, SafeExportChild(destination, child.Name));
            return;
        }
        if (entry.ArchiveItem?.Entry is not RpfFileEntry file)
            throw new ArgumentException($"Archive entry is unavailable: {entry.Name}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, file.ReadStandalone());
    }

    private string GameEntryPath(EntryRecord entry)
    {
        string candidate = GameEntryCandidate(entry);
        if (!Path.Exists(candidate))
            throw new ArgumentException($"Entry no longer exists: {entry.Path}");
        return candidate;
    }

    private string GameEntryCandidate(EntryRecord entry)
    {
        if (gameRoot is null)
            throw new ArgumentException("No GTA V installation is loaded");
        string root = Path.GetFullPath(gameRoot);
        string candidate = Path.GetFullPath(Path.Combine(root, entry.Path));
        if (!IsWithin(root, candidate))
            throw new ArgumentException($"Entry is outside the game folder: {entry.Path}");
        return candidate;
    }

    private static string SafeExportChild(string parent, string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException($"Unsafe archive entry name: '{name}'");
        return Path.Combine(parent, name);
    }

    private ArchiveNode? ArchiveByPrefix(string prefix)
    {
        ArchiveNode? root = RootArchive();
        if (root is null)
            return null;
        var pending = new Stack<ArchiveNode>();
        pending.Push(root);
        while (pending.TryPop(out ArchiveNode? node))
        {
            if (node.Prefix == prefix)
                return node;
            for (int index = node.Children.Count - 1; index >= 0; index--)
                pending.Push(node.Children[index]);
        }
        return null;
    }

    private ArchiveNode? RootArchive() => archive?.Root;

    private void CloseArchiveCore()
    {
        archive?.Close();
        archive = null;
        archivePath = "";
        archiveGamePath = "";
        archiveIsNested = false;
    }

    private string RelativeGamePath(string path)
    {
        if (gameRoot is null)
            return "";
        string root = Path.GetFullPath(gameRoot);
        return IsWithin(root, path) && !string.Equals(Path.TrimEndingDirectorySeparator(root), path, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, path).Replace('\\', '/')
            : "";
    }

    private static bool IsWithin(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        string normalized = Path.TrimEndingDirectorySeparator(candidate);
        return string.Equals(normalizedRoot, normalized, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Total, int Navigable) ChildCounts(DirectoryInfo directory)
    {
        try
        {
            int total = 0, navigable = 0;
            foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
            {
                total++;
                if (item is DirectoryInfo || item.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                    navigable++;
            }
            return (total, navigable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }
}
