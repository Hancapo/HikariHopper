using Avalonia.Input;
using HikariHopper.Core;
using HikariHopper.Core.Search;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

// Entry selection (click, Ctrl/Shift, select-all, marquee), clipboard copies and drag-out staging.
public sealed partial class ExplorerViewModel
{
    private HashSet<int> selectedRows = [];
    private int selectedIndex = -1;
    private int selectionAnchor = -1;
    private string selectedName = "";
    private string selectedSize = "";
    private string selectedPath = "";
    private HashSet<int> marqueeBaseSelection = [];
    private KeyModifiers marqueeModifiers;
    private DragStaging? dragStaging;

    /// <summary>"name" for one entry, "N items" for several, empty without a selection.</summary>
    public string SelectedName => selectedName;

    /// <summary>Formatted total size of the selection, empty without a selection.</summary>
    public string SelectedSize => selectedSize;

    /// <summary>The current (keyboard focus) row, or -1.</summary>
    public int SelectedIndex => selectedIndex;

    public bool HasSelection => selectedRows.Count > 0;

    public int SelectionCount => selectedRows.Count;

    public bool SelectionDeletable => SelectedEntries().Count > 0;

    /// <summary>Selects one row; Ctrl toggles it, Shift extends from the anchor (both: adds the range).</summary>
    public void SelectEntry(int row, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (EntryAt(row) is null)
        {
            ClearSelection();
            return;
        }

        bool control = modifiers.HasFlag(KeyModifiers.Control);
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (shift)
        {
            int anchor = selectionAnchor >= 0 ? selectionAnchor : row;
            var interval = Interval(anchor, row);
            if (control)
                interval.UnionWith(selectedRows);
            SetSelection(interval, row, anchor);
            return;
        }
        if (control)
        {
            var selected = new HashSet<int>(selectedRows);
            if (!selected.Remove(row))
                selected.Add(row);
            SetSelection(selected, selected.Count > 0 ? row : -1, row);
            return;
        }
        SetSelection([row], row, row);
    }

    public void SelectAllEntries()
    {
        if (entries.Count == 0)
            return;
        SetSelection(Enumerable.Range(0, entries.Count).ToHashSet(), Math.Max(selectedIndex, 0), 0);
    }

    public void ClearEntrySelection() => ClearSelection();

    /// <summary>Starts a rubber-band selection; Ctrl toggles and Shift adds to the current selection.</summary>
    public void BeginMarqueeSelection(KeyModifiers modifiers)
    {
        marqueeBaseSelection = new HashSet<int>(selectedRows);
        marqueeModifiers = modifiers;
    }

    /// <summary>List view: the marquee covers the contiguous rows <paramref name="first"/>…<paramref name="last"/>.</summary>
    public void UpdateMarqueeSelection(int first, int last)
    {
        HashSet<int> interval = first >= 0 && last >= 0 ? Interval(first, last) : [];
        ApplyMarqueeSelection(interval, last, first);
    }

    /// <summary>Grid view: the marquee covers exactly <paramref name="rows"/>.</summary>
    public void UpdateMarqueeRows(IEnumerable<int> rows)
    {
        var selected = rows.Where(row => row >= 0 && row < entries.Count).ToHashSet();
        ApplyMarqueeSelection(selected, selected.Count > 0 ? selected.Max() : -1, selected.Count > 0 ? selected.Min() : -1);
    }

    public void EndMarqueeSelection()
    {
        marqueeBaseSelection.Clear();
        marqueeModifiers = KeyModifiers.None;
    }

    private void ApplyMarqueeSelection(HashSet<int> interval, int currentHint, int anchorHint)
    {
        HashSet<int> selected;
        if (marqueeModifiers.HasFlag(KeyModifiers.Control))
        {
            selected = new HashSet<int>(marqueeBaseSelection);
            selected.SymmetricExceptWith(interval);
        }
        else if (marqueeModifiers.HasFlag(KeyModifiers.Shift))
        {
            selected = new HashSet<int>(marqueeBaseSelection);
            selected.UnionWith(interval);
        }
        else
        {
            selected = new HashSet<int>(interval);
        }
        int current = selected.Contains(currentHint) ? currentHint : selected.Count > 0 ? selected.Max() : -1;
        int anchor = interval.Contains(anchorHint) ? anchorHint : interval.Count > 0 ? interval.Min() : -1;
        SetSelection(selected, current, anchor);
    }

    private static HashSet<int> Interval(int first, int last) =>
        Enumerable.Range(Math.Min(first, last), Math.Abs(last - first) + 1).ToHashSet();

    private void SetSelection(HashSet<int> rows, int current, int anchor, bool force = false)
    {
        var selected = rows.Where(row => EntryAt(row) is not null).ToHashSet();
        int resolvedCurrent = EntryAt(current) is not null ? current : -1;
        if (!force && selected.SetEquals(selectedRows) && resolvedCurrent == selectedIndex && anchor == selectionAnchor)
            return;
        selectedRows = selected;
        SetModelSelectedRows(selected);
        selectedIndex = resolvedCurrent;
        selectionAnchor = anchor;
        List<EntryRecord> selection = SelectedEntries();
        if (selection.Count == 1)
        {
            selectedName = selection[0].Name;
            selectedPath = selection[0].Path;
        }
        else if (selection.Count > 0)
        {
            selectedName = $"{selection.Count} items";
            selectedPath = "";
        }
        else
        {
            selectedName = "";
            selectedPath = "";
        }
        selectedSize = selection.Count > 0 ? Formatting.FormatSize(selection.Sum(entry => entry.Size)) : "";
        RaiseAll(SelectionProperties);
    }

    /// <summary>Updates <see cref="EntryItem.Selected"/> only for rows whose state changed.</summary>
    private void SetModelSelectedRows(HashSet<int> rows)
    {
        var selected = rows.Where(row => row >= 0 && row < entries.Count).ToHashSet();
        foreach (int row in modelSelectedRows.Except(selected))
            entries[row].Selected = false;
        foreach (int row in selected.Except(modelSelectedRows))
            entries[row].Selected = true;
        modelSelectedRows.Clear();
        modelSelectedRows.UnionWith(selected);
    }

    private List<EntryRecord> SelectedEntries() =>
        selectedRows.Order().Select(EntryAt).OfType<EntryRecord>().ToList();

    private void ClearSelection()
    {
        marqueeBaseSelection.Clear();
        marqueeModifiers = KeyModifiers.None;
        selectedRows.Clear();
        SetModelSelectedRows([]);
        selectedIndex = -1;
        selectionAnchor = -1;
        selectedName = "";
        selectedSize = "";
        selectedPath = "";
        RaiseAll(SelectionProperties);
    }

    private void SelectEntriesByName(IEnumerable<string> names)
    {
        var keys = names.Select(CaseFold).ToHashSet(StringComparer.Ordinal);
        var rows = Enumerable.Range(0, entries.Count).Where(row => keys.Contains(CaseFold(entries[row].Name))).ToHashSet();
        if (rows.Count > 0)
        {
            int current = rows.Min();
            SetSelection(rows, current, current, force: true);
        }
    }

    /// <summary>Copies the selected names, one per line.</summary>
    public void CopySelectedName()
    {
        List<EntryRecord> selection = SelectedEntries();
        if (selection.Count == 0)
            return;
        _ = AppServices.Clipboard.SetTextAsync(string.Join("\n", selection.Select(entry => entry.Name)));
        SetStatus($"Copied {(selection.Count == 1 ? "name" : $"{selection.Count} names")}");
    }

    /// <summary>Copies full paths: <c>C:\…\file</c> for loose items, <c>archive.rpf::inner/path</c> inside RPFs.</summary>
    public void CopySelectedPath()
    {
        List<EntryRecord> selection = SelectedEntries();
        if (selection.Count == 0)
            return;
        if (SearchShowsLocation)
        {
            CopySearchPaths(SelectedHits());
            return;
        }
        List<string> values = Provider.InArchive
            ? selection.Select(entry => $"{Provider.ArchivePath}::{entry.Path}").ToList()
            : selection.Select(entry => GameChildPath(entry.Path)).ToList();
        _ = AppServices.Clipboard.SetTextAsync(string.Join("\n", values));
        SetStatus($"Copied {(values.Count == 1 ? "path" : $"{values.Count} paths")}");
    }

    /// <summary>
    /// Prepares a native drag of the selection (selecting <paramref name="row"/> first when it is not selected)
    /// and returns the local files to drag as copies, together with <see cref="EntryDragMimeType"/>. Archived
    /// entries are extracted into a staging folder that lives until the next drag or until the tab closes.
    /// Returns an empty list when nothing can be dragged.
    /// </summary>
    public IReadOnlyList<string> StartEntryDrag(int row)
    {
        // Results live all over the game: they are copied with Ctrl+C rather than dragged.
        if (EntryOperationBusy || SearchShowsLocation)
            return [];
        if (!selectedRows.Contains(row))
            SelectEntry(row);
        List<EntryRecord> selection = SelectedEntries();
        if (selection.Count == 0)
            return [];
        List<string> paths;
        try
        {
            ReleaseDragStaging();
            if (Provider.InArchive)
            {
                dragStaging = new DragStaging();
                paths = Provider.ExportEntries(selection, dragStaging.Name);
            }
            else
            {
                paths = Provider.ExportEntries(selection, "");
            }
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not extract selection: {error.Message}");
            return [];
        }
        SetStatus(paths.Count == 1 ? "Dragging one item as a copy" : $"Dragging {paths.Count} items as copies");
        return paths;
    }

    private void ReleaseDragStaging()
    {
        dragStaging?.Cleanup();
        dragStaging = null;
    }

    /// <summary>Whether the one selected item is something <see cref="ActivateEntry"/> opens.</summary>
    public bool CanOpenSelection => SelectedEntries() is [var entry] && Opens(entry);

    /// <summary>
    /// What <see cref="ActivateEntry"/> opens: folders, RPFs, YTD/YDR texture dictionaries, text files, images, and the
    /// 3D viewer's collisions and navmeshes.
    /// </summary>
    internal static bool Opens(EntryRecord entry) =>
        entry.IsDirectory
        || entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)
        || entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)
        || entry.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
        || TextDocument.IsTextName(entry.Name)
        || Textures.TextureImport.IsImagePath(entry.Name)
        || IsModelName(entry.Name);

    /// <summary>Enter/double-click: opens folders, RPFs, YTD/YDR texture dictionaries, text files, images and 3D files.</summary>
    public void ActivateEntry(int row)
    {
        if (EntryOperationBusy)
            return;
        if (SearchShowsLocation)
        {
            if (EntryAt(row)?.NativeEntry is SearchHit hit)
            {
                SelectEntry(row);
                OpenHit(hit, open: true);
            }
            return;
        }
        // From the folder's own search, a folder opened is where the search ends.
        if (searching && EntryAt(row) is { IsDirectory: true })
            EndSearch();
        if (EntryAt(row) is not { } entry)
            return;
        SelectEntry(row);
        if (entry.IsDirectory)
        {
            Navigate(Provider.InArchive ? RpfProvider.EntryLocalPath(entry) : entry.Path);
            return;
        }
        if (entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)
            || entry.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            OpenTextureDictionary(entry);
            return;
        }
        if (TextDocument.IsTextName(entry.Name))
        {
            OpenTextFile(entry);
            return;
        }
        if (Textures.TextureImport.IsImagePath(entry.Name))
        {
            OpenImageFile(entry);
            return;
        }
        if (IsModelName(entry.Name))
        {
            OpenModelFile(entry);
            return;
        }
        if (!entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            return;
        OpenArchiveEntry(entry);
    }

    /// <summary>Reads a text file and hands it to the text viewer, unless it is too big or isn't really text.</summary>
    private void OpenTextFile(EntryRecord entry)
    {
        if (entry.Size > TextDocument.MaxSize)
        {
            SetStatus($"{entry.Name} is too large to view ({Formatting.FormatSize(entry.Size)})");
            return;
        }
        byte[] data;
        try
        {
            data = Provider.ReadEntryBytes(entry);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open {entry.Name}: {error.Message}");
            return;
        }
        if (!TextDocument.LooksLikeText(data))
        {
            SetStatus($"{entry.Name} isn't a text file");
            return;
        }
        TextFileOpened?.Invoke(this, TextDocument.Load(entry.Name, data));
        SetStatus($"Opened {entry.Name}");
    }

    /// <summary>Reads an image file and hands it to the image viewer, which decodes it; unless it is too big.</summary>
    private void OpenImageFile(EntryRecord entry)
    {
        if (entry.Size > ImageViewerViewModel.MaxSize)
        {
            SetStatus($"{entry.Name} is too large to view ({Formatting.FormatSize(entry.Size)})");
            return;
        }
        byte[] data;
        try
        {
            data = Provider.ReadEntryBytes(entry);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open {entry.Name}: {error.Message}");
            return;
        }
        ImageFileOpened?.Invoke(this, new ImageViewerViewModel(entry.Name, data));
        SetStatus($"Opened {entry.Name}");
    }

    /// <summary>Reads a collision or navmesh file and hands it to the 3D viewer; unless it is too big.</summary>
    private void OpenModelFile(EntryRecord entry)
    {
        if (entry.Size > ModelFile.MaxSize)
        {
            SetStatus($"{entry.Name} is too large to view ({Formatting.FormatSize(entry.Size)})");
            return;
        }
        byte[] data;
        try
        {
            data = Provider.ReadEntryBytes(entry);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open {entry.Name}: {error.Message}");
            return;
        }
        ModelFileOpened?.Invoke(this, new ModelFile(entry.Name, data));
        SetStatus($"Opened {entry.Name}");
    }

    private void OpenArchiveEntry(EntryRecord entry)
    {
        bool isNested = entry.ArchiveItem is not null;
        string focusPath = isNested ? NestedArchiveKey(entry.Path) : "";
        string statusText;
        try
        {
            if (isNested)
            {
                Provider.OpenNested(entry);
                statusText = "Opened nested archive";
            }
            else
            {
                Provider.OpenArchive(entry.NativeEntry is FileSystemInfo info ? info.FullName : GameChildPath(entry.Path));
                statusText = $"Opened {entry.Name}";
            }
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open archive: {error.Message}");
            return;
        }
        if (isNested)
            expandedNodes.Add(focusPath);
        else
            ClearArchiveExpansion();
        ResetNavigation();
        ExpandCurrentBranch();
        UpdateTreeFocus(focusPath, -1);
        PublishWorkspace();
        SetStatus(statusText);
    }
}

/// <summary>A temporary folder for dragged archive entries, like Python's <c>TemporaryDirectory</c>.</summary>
internal sealed class DragStaging
{
    public DragStaging()
    {
        Name = Directory.CreateTempSubdirectory("HikariHopper-").FullName;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public string Name { get; }

    /// <summary>Deletes the folder, ignoring errors (a drop target may still hold a file open).</summary>
    public void Cleanup()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        try
        {
            Directory.Delete(Name, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Cleanup();
}
