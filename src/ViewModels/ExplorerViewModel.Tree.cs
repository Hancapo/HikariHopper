using Avalonia.Threading;
using HikariHopper.Core;

namespace HikariHopper.ViewModels;

// The folder tree: a flattened list of expanded nodes keyed by game://, rpf://, archive:// and nested:// paths.
public sealed partial class ExplorerViewModel
{
    private HashSet<string> expandedNodes = new(StringComparer.Ordinal);
    private readonly ArchiveTreeProbe archiveProbe = new();
    private int treeRefreshQueued;
    private string treeFocusPath = "";
    private int treeFocusIndex = -1;
    // The location the tree's focus was last synced for, so arriving somewhere new can be told apart.
    private string treeFocusLocation = "";

    /// <summary>Row of <see cref="TreeModel"/> with keyboard focus, or -1.</summary>
    public int TreeFocusIndex => treeFocusIndex;

    /// <summary>Key of the focused tree row, kept stable while rows are inserted above it.</summary>
    public string TreeFocusPath => treeFocusPath;

    public void FocusTreePath(string path)
    {
        int index = TreeRowForPath(path);
        if (index >= 0)
            UpdateTreeFocus(path, index);
    }

    public void FocusTreeRow(int row)
    {
        if (TreeRowAt(row) is { } item)
            UpdateTreeFocus(item.Path, row);
    }

    /// <summary>Shows the location of a tree key without changing which nodes are expanded.</summary>
    public void NavigateTree(string path)
    {
        if (EntryOperationBusy)
            return;
        if (path.StartsWith("game://", StringComparison.Ordinal))
        {
            string target = path["game://".Length..];
            if (Provider.InArchive)
            {
                Provider.ShowGame();
                ResetNavigation();
                PublishWorkspace();
            }
            Navigate(target);
        }
        else if (path == "archive://.")
        {
            ShowRootArchive(path);
        }
        else if (path.StartsWith("archive://", StringComparison.Ordinal))
        {
            string target = ArchiveKeyPath(path, "archive");
            EntryRecord? entry = Provider.ArchiveEntryFromRoot(target);
            if (entry is null || !entry.IsDirectory)
            {
                SetStatus($"Could not find archive folder: {target}");
                return;
            }
            try
            {
                Provider.ActivateEntryArchive(entry);
            }
            catch (ArgumentException error)
            {
                SetStatus(error.Message);
                return;
            }
            string localPath = RpfProvider.EntryLocalPath(entry);
            ResetNavigation();
            history.Add(localPath);
            historyIndex = 1;
            currentPath = localPath;
            ExpandCurrentBranch();
            UpdateTreeFocus(path, TreeRowForPath(path));
            PublishWorkspace();
        }
        else if (path.StartsWith("rpf://", StringComparison.Ordinal))
        {
            string relative = path["rpf://".Length..];
            if (Provider.ArchiveGamePath == relative)
                ShowRootArchive(path);
            else
                OpenArchive(GameChildPath(relative));
        }
        else if (path.StartsWith("nested://", StringComparison.Ordinal))
        {
            string target = ArchiveKeyPath(path, "nested");
            EntryRecord? entry = Provider.ArchiveEntryFromRoot(target);
            if (entry is null)
            {
                SetStatus($"Could not find nested archive: {target}");
                return;
            }
            OpenArchiveEntry(entry);
        }
    }

    private void ShowRootArchive(string focusPath)
    {
        try
        {
            Provider.ShowRootArchive();
        }
        catch (ArgumentException error)
        {
            SetStatus(error.Message);
            return;
        }
        ResetNavigation();
        ExpandCurrentBranch();
        UpdateTreeFocus(focusPath, -1);
        PublishWorkspace();
        SetStatus($"Browsing {Provider.ArchiveName}");
    }

    /// <summary>Enter/double-click on a tree row: navigates, and toggles folders that have children.</summary>
    public void ActivateTreeRow(int row)
    {
        if (TreeRowAt(row)?.Record is not { } item)
            return;
        NavigateTree(item.Path);
        if (item.Kind != "archive" && item.HasChildren)
            ToggleTreeNode(item.Path);
    }

    /// <summary>Expands or collapses a node; collapsed archives open first.</summary>
    public void ToggleTreeNode(string path)
    {
        if (EntryOperationBusy)
            return;
        if (expandedNodes.Remove(path))
        {
            FocusTreePath(path);
            RefreshTree();
            return;
        }
        if (path.StartsWith("rpf://", StringComparison.Ordinal))
        {
            string relative = path["rpf://".Length..];
            if (Provider.ArchiveGamePath != relative)
            {
                OpenArchive(GameChildPath(relative));
                return;
            }
        }
        if (path.StartsWith("nested://", StringComparison.Ordinal))
        {
            string target = ArchiveKeyPath(path, "nested");
            EntryRecord? entry = Provider.ArchiveEntryFromRoot(target);
            if (entry is null)
            {
                SetStatus($"Could not find nested archive: {target}");
                return;
            }
            OpenArchiveEntry(entry);
            return;
        }
        expandedNodes.Add(path);
        FocusTreePath(path);
        RefreshTree();
    }

    private TreeItem? TreeRowAt(int row) => row >= 0 && row < tree.Count ? tree[row] : null;

    private int TreeRowForPath(string path)
    {
        for (int index = 0; index < tree.Count; index++)
        {
            if (tree[index].Path == path)
                return index;
        }
        return -1;
    }

    private int SelectedTreeRow()
    {
        for (int index = 0; index < tree.Count; index++)
        {
            if (tree[index].Selected)
                return index;
        }
        return -1;
    }

    /// <summary>Publishes <paramref name="rows"/> as removals/insertions of the changed middle, never a reset.</summary>
    private void SetTreeRows(List<TreeRecord> rows)
    {
        int oldCount = tree.Count;
        int newCount = rows.Count;
        bool samePaths = oldCount == newCount;
        for (int index = 0; samePaths && index < newCount; index++)
            samePaths = tree[index].Path == rows[index].Path;
        if (!samePaths)
        {
            int prefix = 0;
            while (prefix < oldCount && prefix < newCount && tree[prefix].Path == rows[prefix].Path)
                prefix++;
            int suffix = 0;
            while (suffix < oldCount - prefix && suffix < newCount - prefix
                && tree[oldCount - suffix - 1].Path == rows[newCount - suffix - 1].Path)
                suffix++;
            tree.RemoveRange(prefix, oldCount - prefix - suffix);
            tree.InsertRange(prefix, rows.GetRange(prefix, newCount - suffix - prefix)
                .Select(row => new TreeItem(row)).ToList());
        }
        for (int index = 0; index < newCount; index++)
            tree[index].Update(rows[index]);
    }

    private void RefreshTree()
    {
        if (!Provider.IsOpen)
        {
            SetTreeRows([]);
            UpdateTreeFocus("", -1);
            return;
        }
        SetTreeRows(PersistentTreeRows());
        SyncTreeFocus();
    }

    private void SyncTreeFocus()
    {
        // Which game and which RPF too: another game's root, or another RPF's, is somewhere new as well.
        string location = $"{Provider.GamePath}|{(Provider.HasArchive ? Provider.ArchivePath : "")}|{Provider.InArchive}|"
            + $"{Provider.ArchiveGamePath}|{Provider.ArchivePrefix}|{currentPath}";
        bool arrived = location != treeFocusLocation;
        treeFocusLocation = location;
        // Arriving at the root, which has no row, leaves no row focused: the folder you came from is not
        // where you are, and Enter must not open it again. A row focused after that keeps its focus.
        if (arrived && NavigationPathSegments.Count == 0)
        {
            UpdateTreeFocus("", -1);
            return;
        }
        int index = TreeRowForPath(treeFocusPath);
        if (index < 0)
            index = SelectedTreeRow();
        // A focused row that went away hands focus to its neighbour. With nothing focused yet, as at the root
        // (which has no row), nothing is: the first folder is not where you are.
        if (index < 0 && tree.Count > 0 && treeFocusPath.Length > 0)
            index = Math.Min(Math.Max(treeFocusIndex, 0), tree.Count - 1);
        UpdateTreeFocus(TreeRowAt(index)?.Path ?? "", index);
    }

    private void UpdateTreeFocus(string path, int index)
    {
        if (path == treeFocusPath && index == treeFocusIndex)
            return;
        treeFocusPath = path;
        treeFocusIndex = index;
        RaiseAll(TreeFocusProperties);
    }

    private List<TreeRecord> PersistentTreeRows()
    {
        // The tree starts with what is inside the root, not the root itself: a tab is one game or one RPF,
        // and the tab already says which. An RPF opened from outside the game is the root while it is open.
        var rows = new List<TreeRecord>();
        if (ArchiveIsRoot)
        {
            if (Provider.ArchiveHasTreeChildren(""))
                AppendArchiveChildren(rows, "", ".", 0);
        }
        else if (Provider.HasGame)
        {
            AppendGameChildren(rows, ".", 0);
        }
        return rows;
    }

    private void AppendGameChildren(List<TreeRecord> rows, string directoryPath, int depth)
    {
        foreach (EntryRecord entry in Provider.GameEntries(directoryPath))
        {
            bool isArchive = !entry.IsDirectory && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
            if (!entry.IsDirectory && !isArchive)
                continue;
            string key = isArchive ? $"rpf://{entry.Path}" : $"game://{entry.Path}";
            bool isLoadedArchive = isArchive && entry.Path == Provider.ArchiveGamePath;
            bool hasChildren = isLoadedArchive
                ? Provider.ArchiveHasTreeChildren("")
                : isArchive
                    ? archiveProbe.HasTreeChildren(Path.Combine(Provider.GamePath, entry.Path), "", Provider.Crypto)
                    : entry.NavigableChildren > 0;
            bool expanded = hasChildren && expandedNodes.Contains(key);
            bool selected = (isLoadedArchive && Provider.InArchive && Provider.ArchivePrefix.Length == 0 && currentPath == ".")
                || (entry.IsDirectory && !Provider.InArchive && currentPath == entry.Path);
            rows.Add(new TreeRecord(entry.Name, key, depth, isArchive ? "archive" : "folder", expanded, hasChildren, selected));
            if (!expanded)
                continue;
            if (entry.IsDirectory)
                AppendGameChildren(rows, entry.Path, depth + 1);
            else if (isLoadedArchive)
                AppendArchiveChildren(rows, "", ".", depth + 1);
        }
    }

    private void AppendArchiveChildren(List<TreeRecord> rows, string archivePrefix, string directoryPath, int depth)
    {
        foreach (EntryRecord entry in Provider.ArchiveEntriesAt(archivePrefix, directoryPath))
        {
            bool isNestedArchive = entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
            if (!entry.IsDirectory && !isNestedArchive)
                continue;
            string key = entry.IsDirectory ? ArchiveDirectoryKey(entry.Path) : NestedArchiveKey(entry.Path);
            string childPrefix = RpfProvider.LoadedNestedPrefix(entry);
            bool hasChildren = entry.IsDirectory
                ? entry.NavigableChildren > 0
                : childPrefix.Length == 0 ? UnopenedArchiveHasChildren(entry) : Provider.ArchiveHasTreeChildren(childPrefix);
            bool expanded = expandedNodes.Contains(key) && (entry.IsDirectory || childPrefix.Length > 0);
            string entryPrefix = RpfProvider.EntryArchivePrefix(entry);
            string localPath = RpfProvider.EntryLocalPath(entry);
            bool selected = (entry.IsDirectory && Provider.InArchive && Provider.ArchivePrefix == entryPrefix
                    && currentPath == localPath)
                || (isNestedArchive && childPrefix.Length > 0 && Provider.InArchive
                    && Provider.ArchivePrefix == childPrefix && currentPath == ".");
            rows.Add(new TreeRecord(entry.Name, key, depth, entry.IsDirectory ? "folder" : "archive",
                expanded, hasChildren, selected));
            if (!expanded)
                continue;
            if (entry.IsDirectory)
                AppendArchiveChildren(rows, archivePrefix, localPath, depth + 1);
            else
                AppendArchiveChildren(rows, childPrefix, ".", depth + 1);
        }
    }

    /// <summary>A nested RPF the explorer hasn't opened: asks the probe, which reads it from its root file.</summary>
    private bool UnopenedArchiveHasChildren(EntryRecord entry) =>
        entry.ArchiveItem?.Node is not { Root.SourcePath: { } rootFile } node
        || archiveProbe.HasTreeChildren(rootFile, entry.Path, node.Crypto);

    /// <summary>The probe found an archive with nothing to expand: rebuild the tree once, on the UI thread.</summary>
    private void OnArchiveProbeFound(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref treeRefreshQueued, 1) == 1)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref treeRefreshQueued, 0);
            if (Provider.IsOpen)
                RefreshTree();
        });
    }

    private void ExpandCurrentBranch()
    {
        if (Provider.InArchive)
        {
            string archiveGamePath = Provider.ArchiveGamePath;
            bool rootHasChildren = Provider.ArchiveHasTreeChildren("");
            string rootKey;
            if (archiveGamePath.Length > 0)
            {
                ExpandGameAncestors(archiveGamePath);
                rootKey = $"rpf://{archiveGamePath}";
            }
            else
            {
                rootKey = "archive://.";
            }
            if (rootHasChildren)
                expandedNodes.Add(rootKey);
            else
                expandedNodes.Remove(rootKey);
            ExpandArchiveAncestors();
        }
        else
        {
            ExpandPathAncestors(currentPath, "game://");
        }
    }

    private void ExpandArchiveAncestors()
    {
        foreach (string prefix in Provider.ArchiveChainPrefixes.Skip(1))
        {
            ExpandArchiveFolders(prefix);
            string key = NestedArchiveKey(prefix);
            if (Provider.ArchiveHasTreeChildren(prefix))
                expandedNodes.Add(key);
            else
                expandedNodes.Remove(key);
        }

        if (currentPath == ".")
            return;
        ExpandArchiveFolders(string.Join('/', new[] { Provider.ArchivePrefix, currentPath }.Where(part => part.Length > 0)));
    }

    /// <summary>Expands every proper ancestor of <paramref name="fullPath"/> that is a folder rather than an RPF.</summary>
    private void ExpandArchiveFolders(string fullPath)
    {
        string[] parts = fullPath.Split('/');
        for (int index = 1; index < parts.Length; index++)
        {
            string candidate = string.Join('/', parts[..index]);
            if (!candidate.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                expandedNodes.Add(ArchiveDirectoryKey(candidate));
        }
    }

    private void ExpandGameAncestors(string path)
    {
        expandedNodes.Add("game://.");
        string[] parentParts = path.Split('/')[..^1];
        for (int index = 1; index <= parentParts.Length; index++)
            expandedNodes.Add($"game://{string.Join('/', parentParts[..index])}");
    }

    private void ExpandPathAncestors(string path, string prefix)
    {
        if (path == ".")
            return;
        string[] parts = path.Split('/');
        for (int index = 1; index < parts.Length; index++)
            expandedNodes.Add($"{prefix}{string.Join('/', parts[..index])}");
    }

    private void ClearArchiveExpansion() =>
        expandedNodes.RemoveWhere(key => key.StartsWith("rpf://", StringComparison.Ordinal)
            || key.StartsWith("archive://", StringComparison.Ordinal)
            || key.StartsWith("nested://", StringComparison.Ordinal));

    private string ArchiveDirectoryKey(string fullPath) => $"archive://{Provider.ArchiveTreeId}::{fullPath}";

    private string NestedArchiveKey(string fullPath) => $"nested://{Provider.ArchiveTreeId}::{fullPath}";

    private string ArchiveKeyPath(string key, string scheme)
    {
        string prefix = $"{scheme}://";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return "";
        string rest = key[prefix.Length..];
        int separator = rest.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0 || rest[..separator] != Provider.ArchiveTreeId)
            return "";
        return rest[(separator + 2)..];
    }

    /// <summary><c>str(Path(game_path) / relative)</c>.</summary>
    private string GameChildPath(string relative) => Path.Combine(Provider.GamePath, relative.Replace('/', '\\'));
}
