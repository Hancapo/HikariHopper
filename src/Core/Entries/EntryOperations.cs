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

    /// <summary>
    /// Renames the file or folder <paramref name="name"/> in <paramref name="target"/> to <paramref name="newName"/>, on
    /// disk or in an RPF. Only a change of case may keep a name another entry there has. Returns the new name.
    /// </summary>
    public static string RenameAt(EntryCreationTarget target, string name, string newName)
    {
        if (!target.InArchive)
        {
            if (target.Directory is null)
                throw new ArgumentException("No writable explorer location is open");
            string source = Path.Combine(target.Directory, name);
            string destination = Path.Combine(target.Directory, newName);
            if (!Path.Exists(source))
                throw new FileNotFoundException($"Not found: {name}", source);
            GameTextures.WaitForArchiveReaders(source);
            bool caseOnly = string.Equals(name, newName, StringComparison.OrdinalIgnoreCase);
            // Every other entry, so a case-only rename is checked too: a case-sensitive folder can hold "pack" and "Pack".
            EntryNames.EnsureEntryNameAvailable(newName,
                new DirectoryInfo(target.Directory).EnumerateFileSystemInfos()
                    .Select(item => item.Name).Where(other => !string.Equals(other, name, StringComparison.Ordinal)));
            if (Directory.Exists(source))
            {
                if (!caseOnly)
                {
                    Directory.Move(source, destination);
                    return newName;
                }
                // A folder can't be moved onto itself in another case; it goes through a free name on the way, and
                // back to its own name if the last move fails.
                string step = Path.Combine(target.Directory, $"{newName}.renaming-{Guid.NewGuid():N}");
                Directory.Move(source, step);
                try
                {
                    Directory.Move(step, destination);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Directory.Move(step, source);
                    throw;
                }
            }
            else
            {
                File.Move(source, destination);
            }
            return newName;
        }
        SaveTargetArchive(target, archive =>
        {
            RpfDirectoryEntry directory = DirectoryAt(archive, target.InternalDirectory)
                ?? throw new ArgumentException($"Could not find archive folder: {target.InternalDirectory}");
            RpfEntry entry = directory.Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Not found: {name}");
            EntryNames.EnsureEntryNameAvailable(newName,
                directory.Children.Where(child => !ReferenceEquals(child, entry)).Select(child => child.Name));
            entry.Name = newName;
        });
        return newName;
    }

    /// <summary>Imports dropped files and folders; a name already at the target is dealt with as <paramref name="conflict"/> says.</summary>
    public static IReadOnlyList<string> ImportFilesAt(EntryCreationTarget target, IEnumerable<string> sources,
        ImportConflict conflict = ImportConflict.Fail) =>
        ImportAt(target, sources, conflict);

    /// <summary>
    /// Copies files and folders (with everything in them) into <paramref name="target"/>, a folder on disk or in an
    /// RPF. With <paramref name="freeNames"/>, as a paste, a name already there becomes "name (2)" and so on;
    /// otherwise it is an error. Returns the names written.
    /// </summary>
    public static IReadOnlyList<string> ImportAt(EntryCreationTarget target, IEnumerable<string> sources, bool freeNames) =>
        ImportAt(target, sources, freeNames ? ImportConflict.KeepBoth : ImportConflict.Fail);

    /// <summary>
    /// Copies files and folders (with everything in them) into <paramref name="target"/>, a folder on disk or in an
    /// RPF; a name already there is an error, a free "name (2)", left out, or replaced, as <paramref name="conflict"/>
    /// says. Replacing a folder merges into it, replacing the files in it with the same names. Returns the names
    /// written.
    /// </summary>
    public static IReadOnlyList<string> ImportAt(EntryCreationTarget target, IEnumerable<string> sources, ImportConflict conflict)
    {
        bool freeNames = conflict == ImportConflict.KeepBoth;
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
            string folderAt = target.Directory;
            var destinations = Place(resolved, taken, name => Directory.Exists(Path.Combine(folderAt, name)), conflict)
                .Select(item => (item.Source, Destination: Path.Combine(folderAt, item.Name), item.Replace)).ToList();
            var created = new List<string>();
            var replaced = new List<(string Original, string Backup)>();
            // Counted file by file; stopped between files, what was copied is undone as for a failure.
            OperationProgress.Current.Expect(destinations.Sum(item => FileCount(item.Source)));
            try
            {
                foreach (var (source, destination, replace) in destinations)
                    CopyLoose(source, destination, created, replaced, replace);
                // A Cancel during the last file is still a Cancel.
                OperationProgress.Current.Check();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
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
                // And the files it replaced are put back as they were.
                for (int index = replaced.Count - 1; index >= 0; index--)
                {
                    try
                    { File.Move(replaced[index].Backup, replaced[index].Original, overwrite: true); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }
                throw;
            }
            foreach (var (_, backup) in replaced)
            {
                try
                { File.Delete(backup); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
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
            var finals = Place(resolved, existing,
                name => directory.Children.Any(child => child is RpfDirectoryEntry && string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase)), conflict);
            // File-backed: FiveFury reads each source while the archive is written right after, so the write is what
            // takes the time, and it grows by about what is added.
            foreach (var (source, name, replace) in finals)
            {
                AddToArchive(archive, EntryNames.ArchiveChildPath(target.InternalDirectory, name), source, replace);
                OperationProgress.Current.ExpectExtraBytes(ByteCount(source));
            }
            written.AddRange(finals.Select(item => item.Name));
        });
        return written;
    }

    /// <summary>
    /// Where each import goes beside <paramref name="taken"/>: its own name when free; otherwise, as
    /// <paramref name="conflict"/> says, an error, a free "name (2)", nowhere (left out), or the one already there,
    /// replaced. A file can't replace a folder, nor a folder a file.
    /// </summary>
    private static List<(string Source, string Name, bool Replace)> Place(List<(string Source, string Name)> resolved,
        List<string> taken, Func<string, bool> folderThere, ImportConflict conflict)
    {
        var placed = new List<(string Source, string Name, bool Replace)>();
        foreach (var (source, name) in resolved)
        {
            bool folder = Directory.Exists(source);
            string? there = taken.FirstOrDefault(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            if (there is null || conflict == ImportConflict.KeepBoth)
            {
                placed.Add((source, Claim(name, folder, taken, freeNames: conflict == ImportConflict.KeepBoth), false));
                continue;
            }
            switch (conflict)
            {
                case ImportConflict.Skip:
                    break;
                case ImportConflict.Replace:
                    if (folderThere(there) != folder)
                        throw new ArgumentException(folder ? $"{there} is a file here; a folder can't replace it" : $"{there} is a folder here; a file can't replace it");
                    placed.Add((source, there, true));
                    break;
                default:
                    throw new EntryExistsException($"{there} already exists");
            }
        }
        return placed;
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

    /// <summary>How many files <paramref name="path"/> is: one for a file, everything in a folder for a folder.</summary>
    private static int FileCount(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count() : 1;

    /// <summary>How many bytes <paramref name="path"/> holds, a folder's files all together.</summary>
    private static long ByteCount(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
        : File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>
    /// Copies a file, or a folder and everything in it, recording each thing it makes in <paramref name="created"/>.
    /// With <paramref name="replace"/>, a file already there is replaced (written beside it, the old one moved aside to
    /// a backup recorded in <paramref name="replaced"/>, then the new one moved in) and a folder already there is merged
    /// into.
    /// </summary>
    private static void CopyLoose(string source, string destination, List<string> created,
        List<(string Original, string Backup)> replaced, bool replace = false)
    {
        if (File.Exists(source))
            OperationProgress.Current.Item(Path.GetFileName(source));
        if (File.Exists(source) && replace && File.Exists(destination))
        {
            string staging = destination + ".hikari-replace";
            string backup = destination + ".hikari-backup";
            File.Copy(source, staging, overwrite: true);
            try
            {
                CopyStat(source, staging);
                GameTextures.WaitForArchiveReaders(destination);
                File.Move(destination, backup, overwrite: true);
            }
            catch
            {
                File.Delete(staging);
                throw;
            }
            // Kept until the import is done, so an undone import can put the old file back.
            replaced.Add((destination, backup));
            // A game's RPF replaced by another has the game's texture tables made again.
            GameTextures.Changed(destination);
            try
            {
                File.Move(staging, destination);
            }
            catch
            {
                File.Delete(staging);
                throw;
            }
            return;
        }
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
        {
            if (!replace)
                throw new IOException($"{Path.GetFileName(destination)} already exists");
        }
        else
        {
            if (File.Exists(destination))
                throw new IOException($"{Path.GetFileName(destination)} is a file here; a folder can't replace it");
            Directory.CreateDirectory(destination);
            created.Add(destination);
        }
        foreach (string file in Directory.EnumerateFiles(source))
            CopyLoose(file, Path.Combine(destination, Path.GetFileName(file)), created, replaced, replace);
        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            if (IsLinkedPath(folder))
                continue;
            CopyLoose(folder, Path.Combine(destination, Path.GetFileName(folder)), created, replaced, replace);
        }
    }

    /// <summary>
    /// Adds a file at <paramref name="path"/> in the archive, or a folder there with everything in it. With
    /// <paramref name="replace"/>, a file already there is replaced and a folder already there is merged into.
    /// </summary>
    private static void AddToArchive(RpfArchive archive, string path, string source, bool replace = false)
    {
        if (File.Exists(source))
        {
            if (replace && archive.Find(path) is { } existing)
            {
                if (existing is RpfDirectoryEntry)
                    throw new ArgumentException($"{existing.Name} is a folder here; a file can't replace it");
                archive.Remove(existing);
            }
            archive.AddFile(path, source);
            return;
        }
        archive.AddDirectory(path);
        foreach (string file in Directory.EnumerateFiles(source))
            AddToArchive(archive, EntryNames.ArchiveChildPath(path, EntryNames.NormalizeEntryName(Path.GetFileName(file))), file, replace);
        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            if (IsLinkedPath(folder))
                continue;
            AddToArchive(archive, EntryNames.ArchiveChildPath(path, EntryNames.NormalizeEntryName(Path.GetFileName(folder))), folder, replace);
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
        // Item by item; stopped between them, what was moved stays in the Recycle Bin, from where it comes back.
        OperationProgress.Current.Expect(target.LoosePaths.Count);
        foreach (string path in target.LoosePaths)
        {
            OperationProgress.Current.Item(Path.GetFileName(path));
            if (!Path.Exists(path))
                continue;
            GameTextures.WaitForArchiveReaders(path);
            if (IsLinkedPath(path) || !RecycleBin.MoveToTrash(path))
            {
                failed.Add(Path.GetFileName(path));
                continue;
            }
            moved++;
            OperationProgress.Current.Succeed();
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

/// <summary>What an import does with a name already at its target.</summary>
public enum ImportConflict
{
    /// <summary>Stops: nothing is imported.</summary>
    Fail,

    /// <summary>Imports it as "name (2)", "name (3)"…, as a paste does.</summary>
    KeepBoth,

    /// <summary>Leaves it out; the rest are imported.</summary>
    Skip,

    /// <summary>Replaces the file already there, or merges a folder into the one there.</summary>
    Replace,
}
