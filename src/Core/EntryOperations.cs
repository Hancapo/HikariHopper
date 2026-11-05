using FiveFury.Rpf;
using FiveFury.Ytd;
using HikariHopper.Infrastructure;

namespace HikariHopper.Core;

/// <summary>Creation, import and deletion of explorer entries, on disk or inside RPF archives.</summary>
public static class EntryOperations
{
    public static string CreateFolderAt(EntryCreationTarget target, string name)
    {
        string normalized = EntryNames.NormalizeEntryName(name);
        if (target.InArchive)
            MutateTargetArchive(target, normalized,
                archive => archive.AddDirectory(EntryNames.ArchiveChildPath(target.InternalDirectory, normalized)));
        else
            Directory.CreateDirectory(LooseDestination(target, normalized));
        return normalized;
    }

    public static string CreateEmptyRpfAt(EntryCreationTarget target, string name)
    {
        string normalized = EntryNames.NormalizeRpfName(name);
        StoreRpf(target, normalized, new RpfArchive(normalized));
        return normalized;
    }

    /// <summary>Creates a YTD with no textures, written natively by FiveFury for the target's edition.</summary>
    public static string CreateYtdAt(EntryCreationTarget target, string name)
    {
        if (target.Game is null)
            throw new ArgumentException("Open a configured game before creating a YTD; its Legacy/Enhanced edition is unknown");
        string normalized = EntryNames.NormalizeYtdName(name);
        StoreFileBytes(target, normalized, new Ytd(target.Game.Value).Write(allowEmpty: true));
        return normalized;
    }

    public static string CreateRpfFromFolderAt(EntryCreationTarget target, string name, string source)
    {
        string sourcePath = Path.GetFullPath(source);
        if (!Directory.Exists(sourcePath))
            throw new ArgumentException($"RPF source folder does not exist: {sourcePath}");
        string normalized = EntryNames.NormalizeRpfName(name);
        StoreRpf(target, normalized, RpfArchive.FromDirectory(sourcePath, normalized));
        return normalized;
    }

    public static string CreateRpfFromZipAt(EntryCreationTarget target, string name, string source)
    {
        string sourcePath = Path.GetFullPath(source);
        if (!File.Exists(sourcePath))
            throw new ArgumentException($"RPF source ZIP does not exist: {sourcePath}");
        string normalized = EntryNames.NormalizeRpfName(name);
        using FileStream input = File.OpenRead(sourcePath);
        try
        {
            using var probe = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException($"Not a valid ZIP archive: {Path.GetFileName(sourcePath)}", exception);
        }
        input.Position = 0;
        RpfArchive archive = RpfArchive.FromZip(input, normalized);
        StoreRpf(target, normalized, archive);
        return normalized;
    }

    /// <summary>Imports dropped files and folders; a name already at the target is an error.</summary>
    public static IReadOnlyList<string> ImportFilesAt(EntryCreationTarget target, IEnumerable<string> sources) =>
        ImportAt(target, sources, freeNames: false);

    /// <summary>
    /// Copies files and folders (with everything in them) into <paramref name="target"/>, a folder on disk or in an
    /// RPF. With <paramref name="freeNames"/>, as a paste, a name already there becomes "name (2)" and so on;
    /// otherwise, as a drop, it is an error. Returns the names written.
    /// </summary>
    public static IReadOnlyList<string> ImportAt(EntryCreationTarget target, IEnumerable<string> sources, bool freeNames)
    {
        var resolved = new List<(string Source, string Name)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in sources)
        {
            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            if (!File.Exists(source) && !Directory.Exists(source))
                throw new FileNotFoundException($"File not found: {source}", source);
            if (Directory.Exists(source) && IsLinkedPath(source))
                throw new ArgumentException("Importing linked folders is not supported");
            string name = EntryNames.NormalizeEntryName(Path.GetFileName(source));
            if (!names.Add(name) && !freeNames)
                throw new ArgumentException($"More than one dropped file is named {name}");
            resolved.Add((source, name));
        }
        if (resolved.Count == 0)
            throw new ArgumentException("Drop one or more files to import");
        if (target.Directory is { } into && !target.InArchive)
        {
            string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(into));
            if (resolved.Any(item => Directory.Exists(item.Source)
                    && (string.Equals(destination, item.Source, StringComparison.OrdinalIgnoreCase)
                        || destination.StartsWith(item.Source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("A folder cannot be copied into itself");
        }

        if (!target.InArchive)
        {
            if (target.Directory is null)
                throw new ArgumentException("No writable explorer location is open");
            var taken = new List<string>(new DirectoryInfo(target.Directory).EnumerateFileSystemInfos().Select(item => item.Name));
            var destinations = new List<(string Source, string Destination)>();
            foreach (var (source, name) in resolved)
            {
                string final = Claim(name, Directory.Exists(source), taken, freeNames);
                destinations.Add((source, Path.Combine(target.Directory, final)));
            }
            var created = new List<string>();
            try
            {
                foreach (var (source, destination) in destinations)
                    CopyLoose(source, destination, created);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Undone deepest first: what a folder held goes before the folder.
                for (int index = created.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        if (Directory.Exists(created[index]))
                            Directory.Delete(created[index]);
                        else
                            File.Delete(created[index]);
                    }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }
                throw;
            }
            return destinations.Select(item => Path.GetFileName(item.Destination)).ToArray();
        }

        string archivePath = Path.GetFullPath(target.ArchivePath!);
        if (resolved.Any(item => string.Equals(item.Source, archivePath, StringComparison.OrdinalIgnoreCase)
                || archivePath.StartsWith(item.Source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("An RPF cannot be imported into itself");

        var written = new List<string>();
        SaveTargetArchive(target, archive =>
        {
            RpfDirectoryEntry directory = DirectoryAt(archive, target.InternalDirectory)
                ?? throw new ArgumentException($"Could not find archive folder: {target.InternalDirectory}");
            var existing = directory.Children.Select(child => child.Name).ToList();
            var finals = resolved.Select(item => (item.Source, Name: Claim(item.Name, Directory.Exists(item.Source), existing, freeNames))).ToList();
            // File-backed: FiveFury reads each source while the archive is written right after.
            foreach (var (source, name) in finals)
                AddToArchive(archive, EntryNames.ArchiveChildPath(target.InternalDirectory, name), source);
            written.AddRange(finals.Select(item => item.Name));
        });
        return written;
    }

    /// <summary>
    /// The name an import takes beside <paramref name="taken"/>: <paramref name="name"/> itself, or with
    /// <paramref name="freeNames"/> the first free "name (2)", "name (3)"…; then taken too. A folder, or a file with
    /// nothing before its dot, is numbered after its whole name, any other file before its extension.
    /// </summary>
    private static string Claim(string name, bool folder, List<string> taken, bool freeNames)
    {
        string final = name;
        if (freeNames)
        {
            string stem = folder ? name : Path.GetFileNameWithoutExtension(name);
            string extension = folder ? "" : Path.GetExtension(name);
            if (stem.Length == 0)
                (stem, extension) = (name, "");
            for (int number = 2; taken.Contains(final, StringComparer.OrdinalIgnoreCase); number++)
                final = $"{stem} ({number}){extension}";
        }
        else
        {
            EntryNames.EnsureEntryNameAvailable(name, taken);
        }
        taken.Add(final);
        return final;
    }

    /// <summary>Copies a file, or a folder and everything in it, recording each thing it makes in <paramref name="created"/>.</summary>
    private static void CopyLoose(string source, string destination, List<string> created)
    {
        if (File.Exists(source))
        {
            using (FileStream input = File.OpenRead(source))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
            {
                created.Add(destination);
                input.CopyTo(output, 1024 * 1024);
            }
            CopyStat(source, destination);
            return;
        }
        if (Directory.Exists(destination))
            throw new IOException($"{Path.GetFileName(destination)} already exists");
        Directory.CreateDirectory(destination);
        created.Add(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            CopyLoose(file, Path.Combine(destination, Path.GetFileName(file)), created);
        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            if (IsLinkedPath(folder))
                continue;
            CopyLoose(folder, Path.Combine(destination, Path.GetFileName(folder)), created);
        }
    }

    /// <summary>Adds a file at <paramref name="path"/> in the archive, or a folder there with everything in it.</summary>
    private static void AddToArchive(RpfArchive archive, string path, string source)
    {
        if (File.Exists(source))
        {
            archive.AddFile(path, source);
            return;
        }
        archive.AddDirectory(path);
        foreach (string file in Directory.EnumerateFiles(source))
            archive.AddFile(EntryNames.ArchiveChildPath(path, EntryNames.NormalizeEntryName(Path.GetFileName(file))), file);
        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            if (IsLinkedPath(folder))
                continue;
            AddToArchive(archive, EntryNames.ArchiveChildPath(path, EntryNames.NormalizeEntryName(Path.GetFileName(folder))), folder);
        }
    }

    public static int DeleteArchiveFilesAt(EntryDeletionTarget target)
    {
        if (!target.InArchive || target.ArchivePaths.Count == 0)
            throw new ArgumentException("No RPF items were selected for deletion");
        SaveTargetArchive(target.Location, archive =>
        {
            var entries = new List<RpfEntry>();
            foreach (string path in target.ArchivePaths)
            {
                string normalized = EntryNames.NormalizedDirectory(path);
                RpfEntry? entry = normalized == "." ? null : archive.Find(normalized);
                if (entry is null || entry.Parent is null)
                    throw new ArgumentException($"Could not find deletable archive item: {path}");
                entries.Add(entry);
            }
            foreach (RpfEntry entry in entries)
                archive.Remove(entry);
        });
        return target.ArchivePaths.Count;
    }

    /// <summary>Moves the selected loose items to the Recycle Bin.</summary>
    public static int MoveFilesToTrash(EntryDeletionTarget target)
    {
        if (target.InArchive || target.LoosePaths.Count == 0)
            throw new ArgumentException("No loose items were selected for deletion");
        int moved = 0;
        var failed = new List<string>();
        foreach (string path in target.LoosePaths)
        {
            if (!Path.Exists(path))
                continue;
            if (IsLinkedPath(path) || !RecycleBin.MoveToTrash(path))
            {
                failed.Add(Path.GetFileName(path));
                continue;
            }
            moved++;
        }
        if (failed.Count > 0)
            throw new IOException($"Could not move to the Recycle Bin: {string.Join(", ", failed)}");
        return moved;
    }

    public static bool IsLinkedPath(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists && !Directory.Exists(path))
                return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static string LooseDestination(EntryCreationTarget target, string name)
    {
        if (target.Directory is null)
            throw new ArgumentException("No writable explorer location is open");
        EntryNames.EnsureEntryNameAvailable(name,
            new DirectoryInfo(target.Directory).EnumerateFileSystemInfos().Select(item => item.Name));
        return Path.Combine(target.Directory, name);
    }

    private static void StoreRpf(EntryCreationTarget target, string name, RpfArchive archive)
    {
        if (!target.InArchive)
        {
            archive.Save(LooseDestination(target, name));
            return;
        }
        byte[] data = archive.Write();
        MutateTargetArchive(target, name,
            owner => owner.AddBinary(EntryNames.ArchiveChildPath(target.InternalDirectory, name), data));
    }

    private static void StoreFileBytes(EntryCreationTarget target, string name, byte[] data)
    {
        if (!target.InArchive)
        {
            using var output = new FileStream(LooseDestination(target, name), FileMode.CreateNew, FileAccess.Write);
            output.Write(data);
            return;
        }
        MutateTargetArchive(target, name,
            archive => ArchiveWriter.AddStandalone(archive, EntryNames.ArchiveChildPath(target.InternalDirectory, name), data));
    }

    private static void MutateTargetArchive(EntryCreationTarget target, string name, Action<RpfArchive> mutation) =>
        SaveTargetArchive(target, archive =>
        {
            RpfDirectoryEntry directory = DirectoryAt(archive, target.InternalDirectory)
                ?? throw new ArgumentException($"Could not find archive folder: {target.InternalDirectory}");
            EntryNames.EnsureEntryNameAvailable(name, directory.Children.Select(child => child.Name));
            mutation(archive);
        });

    private static void SaveTargetArchive(EntryCreationTarget target, Action<RpfArchive> mutation)
    {
        if (target.ArchivePath is null)
            throw new ArgumentException("No writable RPF archive is open");
        ArchiveWriter.Mutate(target.ArchivePath, target.Crypto, target.ArchivePrefix, mutation);
    }

    private static RpfDirectoryEntry? DirectoryAt(RpfArchive archive, string path)
    {
        string normalized = EntryNames.NormalizedDirectory(path);
        return normalized == "." ? archive.Root : archive.Find(normalized) as RpfDirectoryEntry;
    }

    private static void CopyStat(string source, string destination)
    {
        File.SetCreationTimeUtc(destination, File.GetCreationTimeUtc(source));
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        File.SetLastAccessTimeUtc(destination, File.GetLastAccessTimeUtc(source));
        File.SetAttributes(destination, File.GetAttributes(source) & ~FileAttributes.ReparsePoint);
    }
}
