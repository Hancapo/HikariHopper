using FiveFury;
using FiveFury.Assets;
using FiveFury.Cryptography;
using FiveFury.Resources;
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
    private string gameEdition = "";
    private ArchiveKeys? keys;

    public WorkspaceInfo Info
    {
        get
        {
            if (InArchive)
                return new WorkspaceInfo(archive!.Name, archivePath, archive.Format, archive.Platform);
            if (gameRoot is not null)
                return new WorkspaceInfo(Path.GetFileName(gameRoot), gameRoot, GameEdition, "PC");
            return new WorkspaceInfo("No game", "", "—", "PC");
        }
    }

    /// <summary>The open game's edition id (<see cref="GameCatalog"/>), "" without a game.</summary>
    public string GameEditionId => gameRoot is null ? "" : gameEdition;

    /// <summary>The open game's edition as the status bar and tabs name it ("GTA V Legacy"), "" without a game.</summary>
    public string GameEdition => GameCatalog.Edition(GameEditionId)?.ShortName ?? "";

    public bool IsOpen => gameRoot is not null || archive is not null;

    public bool HasGame => gameRoot is not null;

    public bool HasArchive => archive is not null;

    public bool InArchive => inArchive && archive is not null;

    public GameTarget? GameTarget => gameTarget;

    /// <summary>GTA V's keys while a GTA V game is open; null for RDR2 or without a game.</summary>
    public GameCrypto? Crypto => crypto;

    /// <summary>
    /// Whether what's open can be changed: loose files, or a GTA V RPF7 archive. Red Dead Redemption 2's RPF8 archives
    /// are read only, so far.
    /// </summary>
    public bool ArchiveWritable => !InArchive || archive is Rpf7ArchiveNode;

    /// <summary>Why an archive can't be changed, as the explorer says it.</summary>
    public const string ReadOnlyArchive = "Red Dead Redemption 2's archives can't be changed yet";

    /// <summary>The open game's keys, whatever the game; null without a game.</summary>
    public ArchiveKeys? Keys => keys;

    // What an archive of the game, or one opened on its own, is opened with: the game's keys, else GTA V's if they
    // can be found.
    private ArchiveKeys? KeysForArchives => keys ?? (ArchiveKeys?)GameKeys.TryLoadDefault();

    public string GameName => gameRoot is not null ? Path.GetFileName(gameRoot) : "No game";

    public string GamePath => gameRoot ?? "";

    /// <summary>
    /// The file of the open root archive, or "" with none: the same however deep in nested archives you go,
    /// and across a reload.
    /// </summary>
    public string ArchivePath => RootArchive()?.SourcePath ?? "";

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
        string edition = GameSettings.GameEditionForPath(root);
        if (edition == GameSettings.Rdr2Edition)
        {
            // The shipped tables open one build of the game: another can't be read, so it says so.
            if (GameSettings.InstallationProblem(root, edition) is { } problem)
                throw new ArgumentException(problem);
            return new LoadedGame(root.TrimEnd(Path.DirectorySeparatorChar), edition, Rdr2Keys.Bundled);
        }
        GameTarget target = edition switch
        {
            GameSettings.EnhancedEdition => FiveFury.GameTarget.Enhanced,
            GameSettings.LegacyEdition => FiveFury.GameTarget.Legacy,
            _ => throw new ArgumentException("Select the GTA V folder containing GTA5.exe or GTA5_Enhanced.exe"),
        };
        string executable = Path.Combine(root, GameSettings.GameExecutable(edition));
        return new LoadedGame(root.TrimEnd(Path.DirectorySeparatorChar), target, GameKeys.Load(executable));
    }

    public WorkspaceInfo OpenGame(string path) => ApplyLoadedGame(LoadGame(path));

    public WorkspaceInfo ApplyLoadedGame(LoadedGame loaded)
    {
        Close();
        gameRoot = loaded.Root;
        gameEdition = loaded.Edition;
        keys = loaded.Keys;
        // FiveFury's own view of the game, GTA V's only.
        gameTarget = loaded.IsGtaV ? loaded.Target : null;
        crypto = loaded.IsGtaV ? loaded.Crypto : null;
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
        ArchiveNode opened = ArchiveNode.OpenRoot(resolved, KeysForArchives);
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
            throw new ArgumentException("No game is loaded");
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
        gameEdition = "";
        keys = null;
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
            if (!ArchiveWritable)
                throw new ArgumentException(ReadOnlyArchive);
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
                Crypto: (root as Rpf7ArchiveNode)?.Crypto);
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
            if (entries.Any(entry => ReferenceEquals(entry.ArchiveItem!.Entry, entry.ArchiveItem.Node.RootDirectory)))
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
        OperationProgress.Current.Expect(entries.Sum(FileCount));
        foreach (EntryRecord entry in entries)
        {
            string destination = SafeExportChild(root, entry.Name);
            ExportArchiveEntry(entry, destination);
            exported.Add(destination);
        }
        return exported;
    }

    /// <summary>
    /// Copies <paramref name="entries"/> into <paramref name="destination"/> as files and folders: archived ones out of
    /// the archive (standalone, as a drag gives them), loose ones from the game folder. A name already there gets a
    /// " (2)" suffix rather than being overwritten. Returns what was written, in order.
    /// </summary>
    public List<string> ExtractEntries(IReadOnlyList<EntryRecord> entries, string destination)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        var written = new List<string>();
        // Counted file by file, and stopped between files; what was written stays.
        OperationProgress.Current.Expect(entries.Sum(FileCount));
        foreach (EntryRecord entry in entries)
        {
            string target = FreeChild(root, SafeExportChild(root, entry.Name), entry.IsDirectory);
            if (InArchive)
                ExportArchiveEntry(entry, target);
            else
                CopyLoose(GameEntryPath(entry), target);
            written.Add(target);
        }
        return written;
    }

    /// <summary>
    /// Writes everything in the RPF <paramref name="entry"/> (loose, or nested in the open archive) into a new folder
    /// named after it in <paramref name="destination"/>, keeping its folders; nested RPFs inside stay files.
    /// Returns the folder.
    /// </summary>
    public string ExtractArchiveContents(EntryRecord entry, string destination)
    {
        if (entry.IsDirectory || !entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{entry.Name} is not an RPF archive");
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        string target = FreeChild(root, Path.Combine(root, Path.GetFileNameWithoutExtension(entry.Name)), folder: true);
        if (InArchive)
        {
            if (entry.ArchiveItem is not { Entry.IsBinaryFile: true } item)
                throw new ArgumentException($"Archive entry is unavailable: {entry.Name}");
            ArchiveNode nested;
            try
            { nested = item.Node.OpenNested(item.Entry); }
            catch (InvalidDataException)
            { throw new ArgumentException($"FiveFury could not decode {entry.Name} as a nested RPF archive"); }
            ExportArchiveContents(nested, target);
            return target;
        }
        ArchiveNode opened = ArchiveNode.OpenRoot(GameEntryPath(entry), KeysForArchives);
        try
        { ExportArchiveContents(opened, target); }
        finally
        { opened.Close(); }
        return target;
    }

    private void ExportArchiveContents(ArchiveNode node, string target)
    {
        Directory.CreateDirectory(target);
        EntryRecord[] children = [.. ArchiveRecords(node, ".")];
        OperationProgress.Current.Expect(children.Sum(FileCount));
        foreach (EntryRecord child in children)
            ExportArchiveEntry(child, SafeExportChild(target, child.Name));
    }

    /// <summary>How many files <paramref name="entry"/> is: one for a file, everything in a folder for a folder.</summary>
    private int FileCount(EntryRecord entry)
    {
        if (!entry.IsDirectory)
            return 1;
        if (entry.ArchiveItem is { } item)
            return ArchiveRecords(item.Node, item.LocalPath).Sum(FileCount);
        string path = GameEntryCandidate(entry);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count() : 0;
    }

    /// <summary>
    /// <paramref name="path"/>, or "name (2)", "name (3)"… beside it when something is already there. A folder is
    /// numbered after its whole name, a file before its extension.
    /// </summary>
    private static string FreeChild(string parent, string path, bool folder)
    {
        if (!Path.Exists(path))
            return path;
        string name = Path.GetFileName(path);
        string stem = folder ? name : Path.GetFileNameWithoutExtension(name);
        string extension = folder ? "" : Path.GetExtension(name);
        if (stem.Length == 0)
            (stem, extension) = (name, "");
        for (int number = 2; ; number++)
        {
            string candidate = Path.Combine(parent, $"{stem} ({number}){extension}");
            if (!Path.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>Copies a loose file, or a folder and everything in it, to <paramref name="target"/>.</summary>
    private static void CopyLoose(string source, string target)
    {
        if (File.Exists(source))
        {
            if (EntryOperations.IsLinkedPath(source))
                throw new ArgumentException("Extraction through linked files or folders is not supported");
            OperationProgress.Current.Item(Path.GetFileName(source));
            File.Copy(source, target);
            return;
        }
        string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        string to = Path.GetFullPath(target);
        if (to.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Cannot extract {Path.GetFileName(from)} into itself");
        if (EntryOperations.IsLinkedPath(from))
            throw new ArgumentException("Extraction through linked files or folders is not supported");
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from))
            CopyLoose(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (string folder in Directory.EnumerateDirectories(from))
            CopyLoose(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    /// <summary>
    /// What the Properties sheet shows about <paramref name="entry"/>: where it is, its size (in the archive and
    /// unpacked, for a compressed one), and for a resource its format and contents, for an RPF its encryption and
    /// files. Reads the file for some of it, so it runs off the UI thread.
    /// </summary>
    public EntryProperties DescribeEntry(EntryRecord entry)
    {
        var sections = new List<PropertySection>();
        var location = new List<(string, string)>();
        if (InArchive)
        {
            location.Add(("Archive", archiveGamePath.Length > 0 ? archiveGamePath : archivePath));
            location.Add(("Path", entry.Path));
        }
        else
        {
            string folder = Path.GetDirectoryName(entry.Path.Replace('/', Path.DirectorySeparatorChar)) ?? "";
            location.Add(("Folder", folder is "" or "." ? "Game folder" : folder));
            // Throws when it has gone since it was listed: the read fails, rather than show what it was.
            location.Add(("Full path", GameEntryPath(entry)));
        }
        sections.Add(new PropertySection("LOCATION", location));

        if (entry.IsDirectory)
        {
            sections.Add(new PropertySection("CONTENTS", [("Items", entry.Children == 1 ? "1 item" : $"{entry.Children} items")]));
            return new EntryProperties(entry.Name, "Folder", "folder", sections);
        }

        (string Format, int Version, long TotalSize)? header = ResourceOf(entry);
        var size = new List<(string, string)>();
        // A resource (YTD, YDR and the rest) is kept deflated in an archive; a binary file only when its entry says so.
        if (entry.ArchiveItem?.Entry is { IsDirectory: false, Compressed: true } stored)
        {
            size.Add(("In the archive", $"{Formatting.FormatSize(stored.StoredSize)} · compressed"));
            size.Add(("Unpacked", Formatting.FormatSize(entry.Size)));
        }
        else
        {
            size.Add(("Size", Formatting.FormatSize(entry.Size)));
            // A resource on disk is kept compressed too.
            if (!InArchive && header is { } loose && loose.TotalSize > 0)
                size.Add(("Unpacked", Formatting.FormatSize(loose.TotalSize)));
        }
        sections.Add(new PropertySection("SIZE", size));

        if (header is { } resource)
        {
            var rows = new List<(string, string)> { ("Format", $"{resource.Format} · version {resource.Version}") };
            if (GameEdition.Length > 0)
                rows.Add(("Game", GameEdition));
            if (entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) && TextureCount(entry) is { } textures)
                rows.Add(("Contents", textures == 1 ? "1 texture" : $"{textures} textures"));
            sections.Add(new PropertySection("RESOURCE", rows));
        }

        if (entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && DescribeArchive(entry) is { } archiveRows)
            sections.Add(new PropertySection("ARCHIVE", archiveRows));
        string glyph = entry.Kind.Contains("Package", StringComparison.Ordinal) ? "archive" : "file";
        return new EntryProperties(entry.Name, entry.Kind, glyph, sections);
    }

    // A resource's format, version and unpacked size, as its header records them: in an archive, the entry's, the
    // archive saying which format; on disk, a GTA V resource's header (its size only known there).
    private (string Format, int Version, long TotalSize)? ResourceOf(EntryRecord entry)
    {
        if (InArchive)
            return entry.ArchiveItem is { Entry.ResourceVersion: int version } item ? (item.Node.ResourceFormat, version, 0) : null;
        try
        {
            using FileStream stream = File.OpenRead(GameEntryPath(entry));
            Span<byte> start = stackalloc byte[ResourceHeader.ByteSize];
            if (stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) < start.Length
                || BitConverter.ToUInt32(start) is not (ResourceHeader.Magic or RedFury.Resources.Rsc8Header.Magic))
                return null;
            // RDR2's: its RSC8 header, the version and unpacked size from its flags.
            if (BitConverter.ToUInt32(start) == RedFury.Resources.Rsc8Header.Magic)
            {
                RedFury.Resources.Rsc8Header rsc8 = RedFury.Resources.Rsc8Header.Read(start);
                return ("RSC8", rsc8.ResourceVersion, (long)rsc8.LogicalSize);
            }
            ResourceHeader header = ResourceHeader.Read(start);
            return ("RSC7", (int)header.Version, header.TotalSize);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    private int? TextureCount(EntryRecord entry)
    {
        try
        {
            return Textures.YtdSaving.Read(ReadEntryBytes(entry)).Textures.Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private List<(string, string)>? DescribeArchive(EntryRecord entry)
    {
        ArchiveNode? opened = null;
        try
        {
            ArchiveNode node;
            if (InArchive)
            {
                if (entry.ArchiveItem is not { Entry.IsBinaryFile: true } item)
                    return null;
                node = item.Node.OpenNested(item.Entry);
            }
            else
            {
                node = opened = ArchiveNode.OpenRoot(GameEntryPath(entry), KeysForArchives);
            }
            ArchiveEntry[] entries = [.. node.EnumerateEntries()];
            int files = entries.Count(item => !item.IsDirectory);
            int folders = entries.Length - files;
            return
            [
                ("Encryption", node.Encryption),
                ("Contains", $"{(files == 1 ? "1 file" : $"{files} files")} in {(folders == 1 ? "1 folder" : $"{folders} folders")}"),
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            opened?.Close();
        }
    }

    /// <summary>Reads one loose or archived file as a standalone byte stream.</summary>
    public byte[] ReadEntryBytes(EntryRecord entry)
    {
        if (entry.IsDirectory)
            throw new ArgumentException($"Cannot read a directory as a file: {entry.Name}");
        if (InArchive)
        {
            if (entry.ArchiveItem?.Entry is not { IsDirectory: false } file)
                throw new ArgumentException($"Archive entry is unavailable: {entry.Name}");
            return file.Read();
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
        if (item.Entry.IsDetached)
            throw new ArgumentException("The selected archive entry is detached");
        if (item.Node.Root is not Rpf7ArchiveNode)
            throw new ArgumentException(ReadOnlyArchive);
        ArchiveNode root = item.Node.Root;
        string rootPath = Path.GetFullPath(root.SourcePath ?? "");
        if (!File.Exists(rootPath))
            throw new ArgumentException("The root RPF archive is no longer available");
        // Saving into an archive is GTA V's only, so far: an RPF7 archive's keys.
        return new ArchiveEntryTarget(rootPath, item.FullPath, (root as Rpf7ArchiveNode)?.Crypto, root);
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
        ArchiveNode reloadedRoot = ArchiveNode.OpenRoot(expected, keys ?? root.Keys);
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
        if (archive is null || entry.ArchiveItem is not { Entry.IsBinaryFile: true } item)
            throw new ArgumentException("The selected entry does not contain a nested archive");
        if (!ReferenceEquals(item.Node.Root, archive.Root))
            throw new ArgumentException("The selected entry does not belong to the open RPF");
        ArchiveNode nested;
        try
        { nested = item.Node.OpenNested(item.Entry); }
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
        string extension = Path.GetExtension(name);
        if (GeneralFileKindLabels.TryGetValue(extension, out label))
            return label;
        // What the image and text viewers open, named so the list shows what they are.
        if (Textures.TextureImport.IsImagePath(name))
            return extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? "DDS Texture"
                : extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? "JPEG Image"
                : $"{extension[1..].ToUpperInvariant()} Image";
        return TextDocument.IsTextName(name) ? "Text Document" : "File";
    }

    private static List<EntryRecord> ArchiveRecords(ArchiveNode node, string directoryPath)
    {
        // Closing only releases the file handle; the table stays listable, as in FiveFury Python.
        if (node.DirectoryAt(directoryPath) is not { } directory)
            return [];
        // Folders first, then files, each in the archive's order.
        return directory.Children.Where(item => item.IsDirectory)
            .Concat(directory.Children.Where(item => !item.IsDirectory))
            .Select(item => ArchiveRecord(node, item))
            .ToList();
    }

    private static EntryRecord ArchiveRecord(ArchiveNode node, ArchiveEntry item)
    {
        var native = new ArchiveItem(node, item);
        if (item.IsDirectory)
        {
            int folders = item.Children.Count(child => child.IsDirectory);
            int navigable = folders + item.Children.Count(child =>
                !child.IsDirectory && child.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
            return new EntryRecord(item.Name, native.FullPath, EntryRecord.FolderKind, 0,
                item.Children.Count, native, navigable);
        }
        return new EntryRecord(item.Name, native.FullPath, FileKind(item.Name), item.Size, 0, native,
            Storage: item.Compressed ? EntryStorage.Compressed : EntryStorage.Uncompressed, StoredSize: item.StoredSize);
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
        if (entry.ArchiveItem?.Entry is not { IsDirectory: false } file)
            throw new ArgumentException($"Archive entry is unavailable: {entry.Name}");
        OperationProgress.Current.Item(entry.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, file.Read());
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
            throw new ArgumentException("No game is loaded");
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
