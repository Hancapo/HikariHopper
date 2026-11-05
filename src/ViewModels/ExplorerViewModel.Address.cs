using HikariHopper.Core;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

/// <summary>
/// The address bar as text: where you are, written as a path, and going to a path typed or pasted into it. A path
/// can run through RPFs as if they were folders (<c>…\dlc.rpf\stream</c>), use Copy path's <c>archive.rpf::inner</c>
/// form, or be relative to the game folder.
/// </summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>
    /// Where you are, as one path: a folder on disk, or the archive's file followed by the folders inside it,
    /// through any nested RPFs. Empty with nothing open.
    /// </summary>
    public string AddressText
    {
        get
        {
            if (!Provider.IsOpen)
                return "";
            string local = currentPath == "." ? "" : currentPath;
            if (!Provider.InArchive)
                return local.Length == 0 ? Path.TrimEndingDirectorySeparator(Provider.GamePath) : GameChildPath(local);
            string inner = string.Join('/', new[] { Provider.ArchivePrefix, local }.Where(part => part.Length > 0));
            string archive = Provider.ArchivePath;
            return inner.Length == 0 ? archive : $"{archive}\\{inner.Replace('/', '\\')}";
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
            if (!Path.IsPathRooted(path))
            {
                if (!HasGame)
                {
                    SetStatus("Type a full path, or open a game for paths inside it");
                    return false;
                }
                path = Path.Combine(Provider.GamePath, path);
            }
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
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
        bool inGame = root.Length > 0 && (SamePath(folder, root)
            || folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
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
}
