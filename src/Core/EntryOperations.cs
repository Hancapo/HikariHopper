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

    public static IReadOnlyList<string> ImportFilesAt(EntryCreationTarget target, IEnumerable<string> sources)
    {
        var resolved = new List<(string Source, string Name)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in sources)
        {
            string source = Path.GetFullPath(value);
            if (!File.Exists(source))
            {
                if (Directory.Exists(source))
                    throw new ArgumentException($"Only files can be imported: {Path.GetFileName(source)}");
                throw new FileNotFoundException($"File not found: {source}", source);
            }
            string name = EntryNames.NormalizeEntryName(Path.GetFileName(source));
            if (!names.Add(name))
                throw new ArgumentException($"More than one dropped file is named {name}");
            resolved.Add((source, name));
        }
        if (resolved.Count == 0)
            throw new ArgumentException("Drop one or more files to import");

        if (!target.InArchive)
        {
            var destinations = resolved.Select(item => (item.Source, Destination: LooseDestination(target, item.Name))).ToList();
            var created = new List<string>();
            try
            {
                foreach (var (source, destination) in destinations)
                {
                    using (FileStream input = File.OpenRead(source))
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                    {
                        created.Add(destination);
                        input.CopyTo(output, 1024 * 1024);
                    }
                    CopyStat(source, destination);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                foreach (string destination in created)
                {
                    try
                    { File.Delete(destination); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }
                throw;
            }
            return resolved.Select(item => item.Name).ToArray();
        }

        string archivePath = Path.GetFullPath(target.ArchivePath!);
        if (resolved.Any(item => string.Equals(item.Source, archivePath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("An RPF cannot be imported into itself");

        SaveTargetArchive(target, archive =>
        {
            RpfDirectoryEntry directory = DirectoryAt(archive, target.InternalDirectory)
                ?? throw new ArgumentException($"Could not find archive folder: {target.InternalDirectory}");
            var existing = directory.Children.Select(child => child.Name).ToList();
            foreach (var (_, name) in resolved)
                EntryNames.EnsureEntryNameAvailable(name, existing);
            // File-backed: FiveFury reads each source while the archive is written right after.
            foreach (var (source, name) in resolved)
                archive.AddFile(EntryNames.ArchiveChildPath(target.InternalDirectory, name), source);
        });
        return resolved.Select(item => item.Name).ToArray();
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
