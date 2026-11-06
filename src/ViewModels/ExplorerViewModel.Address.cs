using HikariHopper.Core;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

/// <summary>
/// The address bar as text: where you are, written as a path relative to the game folder, and going to a path typed
/// or pasted into it. A path runs through RPFs as if they were folders (<c>x64\…\dlc.rpf\stream</c>); a full path
/// and Copy path's <c>archive.rpf::inner</c> form work too.
/// </summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>
    /// Where you are, as one path relative to the game folder: the folder, or the RPF followed by the folders inside
    /// it, through any nested RPFs. An RPF from outside the game starts the path itself. Empty at the game's root,
    /// and with nothing open.
    /// </summary>
    public string AddressText
    {
        get
        {
            if (!Provider.IsOpen)
                return "";
            string local = currentPath == "." ? "" : currentPath;
            if (!Provider.InArchive)
                return local.Replace('/', '\\');
            string archive = Provider.ArchiveGamePath.Length > 0 ? Provider.ArchiveGamePath : Path.GetFileName(Provider.ArchivePath);
            return string.Join('\\', new[] { archive, Provider.ArchivePrefix, local }.Where(part => part.Length > 0)).Replace('/', '\\');
        }
    }

    /// <summary>Copies <see cref="AddressText"/>.</summary>
    public void CopyAddress()
    {
        string address = AddressText;
        if (address.Length == 0)
            return;
        _ = AppServices.Clipboard.SetTextAsync(address);
        SetStatus("Copied address");
    }

    /// <summary>
    /// Goes to <paramref name="text"/>: a folder shows its content, an RPF (or a folder inside one) opens there, and a
    /// file shows its folder with the file selected. False, with the reason in the status bar, when it can't.
    /// </summary>
    public bool GoToAddress(string text)
    {
        if (EntryOperationBusy)
            return false;
        string typed = text.Trim().Trim('"').Trim();
        if (typed.Length == 0)
            return false;
        string full;
        try
        {
            string path = typed.Replace("::", "\\").Replace('/', '\\');
            if (Path.IsPathRooted(path))
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            else
            {
                // Relative: to the game folder, or, for an RPF opened from outside it, a path that starts with its name.
                var bases = new List<string>();
                if (HasGame)
                    bases.Add(Provider.GamePath);
                string first = path.Split('\\', 2)[0];
                if (Provider.InArchive && Provider.ArchiveGamePath.Length == 0
                    && string.Equals(first, Path.GetFileName(Provider.ArchivePath), StringComparison.OrdinalIgnoreCase))
                    bases.Add(Path.GetDirectoryName(Provider.ArchivePath)!);
                if (bases.Count == 0)
                {
                    SetStatus($"Type a path inside the game, or a full path: {typed}");
                    return false;
                }
                string[] candidates = [.. bases.Select(folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(folder, path))))];
                full = candidates.FirstOrDefault(Reachable) ?? candidates[0];
            }
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetStatus($"Not a path: {typed}");
            return false;
        }

        if (Directory.Exists(full))
            return GoToFolder(full, typed);
        if (File.Exists(full) && !IsArchiveFile(full))
        {
            string folder = Path.GetDirectoryName(full)!;
            if (!GoToFolder(folder, typed))
                return false;
            SelectEntriesByName([Path.GetFileName(full)]);
            return true;
        }

        // An RPF, or something inside one: the deepest existing file on the way is the archive.
        string? archivePath = null;
        for (string? step = full; step is not null; step = Path.GetDirectoryName(step))
        {
            if (File.Exists(step))
            {
                archivePath = IsArchiveFile(step) ? step : null;
                break;
            }
            if (Directory.Exists(step))
                break;
        }
        if (archivePath is null)
        {
            SetStatus($"Not found: {typed}");
            return false;
        }
        string inner = full.Length > archivePath.Length ? full[(archivePath.Length + 1)..].Replace('\\', '/') : "";
        if (!(Provider.InArchive && SamePath(Provider.ArchivePath, archivePath)))
        {
            OpenArchive(archivePath);
            if (!(Provider.InArchive && SamePath(Provider.ArchivePath, archivePath)))
                return false;
        }
        if (inner.Length == 0)
        {
            NavigateTree("archive://.");
            return true;
        }
        EntryRecord? entry = Provider.ArchiveEntryFromRoot(inner);
        if (entry is null)
        {
            SetStatus($"Not found in {Path.GetFileName(archivePath)}: {inner}");
            return false;
        }
        if (entry.IsDirectory)
        {
            NavigateTree(ArchiveDirectoryKey(inner));
            return true;
        }
        if (IsArchiveFile(entry.Name))
        {
            NavigateTree(NestedArchiveKey(inner));
            return true;
        }
        // A file inside the archive: its folder, with it selected.
        int slash = inner.LastIndexOf('/');
        NavigateTree(slash < 0 ? "archive://." : ArchiveDirectoryKey(inner[..slash]));
        SelectEntriesByName([entry.Name]);
        return true;
    }

    private bool GoToFolder(string folder, string typed)
    {
        string root = HasGame ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(Provider.GamePath)) : "";
        // A drive root keeps its separator (D:\), so it isn't doubled when the game is installed there.
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        bool inGame = root.Length > 0 && (SamePath(folder, root)
            || folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!inGame)
        {
            SetStatus($"Only folders inside the game can be shown here: {typed}");
            return false;
        }
        string relative = SamePath(folder, root) ? "." : Path.GetRelativePath(root, folder).Replace('\\', '/');
        NavigateTree($"game://{relative}");
        return true;
    }

    private static bool IsArchiveFile(string path) => path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="full"/> is a folder or file on disk, or lies inside an RPF that is.</summary>
    private static bool Reachable(string full)
    {
        for (string? step = full; step is not null; step = Path.GetDirectoryName(step))
        {
            if (Directory.Exists(step))
                return step == full;
            if (File.Exists(step))
                return step == full || IsArchiveFile(step);
        }
        return false;
    }
}
