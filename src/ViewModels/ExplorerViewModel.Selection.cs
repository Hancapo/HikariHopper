using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;
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
    /// <summary>
    /// Where Windows Explorer goes for <see cref="ShowInExplorer"/>: the one item selected, shown selected in its folder;
    /// with nothing selected, the folder shown, opened. Explorer can't look inside an RPF, so for anything in one it is
    /// the RPF on disk, shown selected. None for several items, or with nothing on disk to go to.
    /// </summary>
    internal (string Path, bool Select)? ExplorerTarget()
    {
        if (!HasWorkspace)
            return null;
        List<EntryRecord> selection = SelectedEntries();
        if (selection.Count > 1)
            return null;
        if (selection.Count == 1 && SearchShowsLocation)
        {
            if (SelectedHits() is not [SearchHit hit])
                return null;
            HitPlace place = PlaceOf(hit);
            return (place.InArchive ? place.RootFull : place.LooseFull, true);
        }
        if (Provider.InArchive)
            return Provider.ArchivePath.Length > 0 ? (Provider.ArchivePath, true) : null;
        if (Provider.GamePath.Length == 0)
            return null;
        return selection.Count == 1
            ? (Path.GetFullPath(GameChildPath(selection[0].Path)), true)
            : (Path.TrimEndingDirectorySeparator(Path.GetFullPath(GameChildPath(CurrentPath))), false);
    }

    /// <summary>Whether <see cref="ShowInExplorer"/> has somewhere to go.</summary>
    public bool CanShowInExplorer => ExplorerTarget() is not null;

    /// <summary>Opens Windows Explorer at a path, with it selected in its folder or opened; tests replace it.</summary>
    internal static Action<string, bool> RevealInExplorer { get; set; } = (path, select) =>
    {
        var start = new ProcessStartInfo("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"") { UseShellExecute = false };
        Process.Start(start)?.Dispose();
    };

    /// <summary>
    /// Shows the selected item in Windows Explorer, or opens the folder shown when nothing is selected (see
    /// <see cref="ExplorerTarget"/>).
    /// </summary>
    public void ShowInExplorer()
    {
        if (ExplorerTarget() is not { } target)
            return;
        if (!(target.Select ? File.Exists(target.Path) || Directory.Exists(target.Path) : Directory.Exists(target.Path)))
        {
            SetStatus($"Not found on disk: {Path.GetFileName(target.Path)}");
            return;
        }
        try
        {
            RevealInExplorer(target.Path, target.Select);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open Explorer: {error.Message}");
        }
    }

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
    /// What <see cref="ActivateEntry"/> opens: folders, RPFs, YTD texture dictionaries, text files, images, and the 3D
    /// viewer's collisions, navmeshes and drawables.
    /// </summary>
    internal static bool Opens(EntryRecord entry) =>
        entry.IsDirectory
        || entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)
        || entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)
        || TextDocument.IsTextName(entry.Name)
        || Textures.TextureImport.IsImagePath(entry.Name)
        || IsModelName(entry.Name);

    /// <summary>Enter/double-click: opens folders, RPFs, YTD texture dictionaries, text files, images and 3D files.</summary>
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
        if (entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
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
        TextFileOpened?.Invoke(this, new TextFile(TextDocument.Load(entry.Name, data)) { SaveTarget = SaveTargetOf(entry) });
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

    /// <summary>The largest 3D file the explorer opens, and its embedded textures read again; lowered by tests.</summary>
    internal long ModelSizeLimit { get; set; } = ModelFile.MaxSize;

    /// <summary>Reads a collision, navmesh or drawable file and hands it to the 3D viewer; unless it is too big.</summary>
    private void OpenModelFile(EntryRecord entry)
    {
        if (entry.Size > ModelSizeLimit)
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
        ModelFileOpened?.Invoke(this, new ModelFile(entry.Name, data)
        {
            Dictionaries = DictionariesBeside(entry),
            GameDictionaries = GameDictionaries(entry.Name),
            GamePaints = GamePaints(entry.Name),
            GameSky = LoadedGameOfProvider() is { } game ? SkyOf(game) : null,
            OpenTextures = OpensTexturesOf(entry, data),
            Nearby = NearbyDictionaries(entry),
            SaveTarget = SaveTargetOf(entry),
        });
        SetStatus($"Opened {entry.Name}");
    }

    /// <summary>
    /// The game's dictionaries for a drawable's textures (<see cref="GameTextures"/>): the ones its archetypes name and
    /// their parents, each read when the search reaches it, with a reader of its own, off the UI thread. None outside a
    /// game, for other files, or for a drawable no archetype names.
    /// </summary>
    /// <summary>The game's car colours and a vehicle's combinations of them, for a fragment opened from a game.</summary>
    private Task<Rendering.CarPaints?>? GamePaints(string modelName) =>
        modelName.EndsWith(".yft", StringComparison.OrdinalIgnoreCase) && LoadedGameOfProvider() is { } game
            ? GameTextures.ForGame(game).PaintsFor(modelName) : null;

    private Task<IReadOnlyList<Rendering.NearbyDictionary>>? GameDictionaries(string modelName)
    {
        if (!ModelFile.IsDrawableName(modelName) || LoadedGameOfProvider() is not { } game)
            return null;
        return Find(game, modelName);

        static async Task<IReadOnlyList<Rendering.NearbyDictionary>> Find(LoadedGame game, string modelName) =>
            [.. (await GameTextures.ForGame(game).DictionariesFor(modelName).ConfigureAwait(false)).Select(dictionary =>
                new Rendering.NearbyDictionary(Path.GetFileName(dictionary.Path), dictionary.Size, () => ReadGameFile(game, dictionary.Path),
                    Rendering.NearbyDictionary.KeyForGameFile(game.Root, dictionary.Path)))];
    }

    /// <summary>
    /// Where the game's <c>time.xml</c> is, in the order the game would take it: a mod's update, the game's update, then
    /// the one in common.rpf, which both editions ship.
    /// </summary>
    internal static readonly string[] TimeXmlPaths =
        ["mods/update/update.rpf/common/data/levels/gta5/time.xml", "update/update.rpf/common/data/levels/gta5/time.xml", "common.rpf/data/levels/gta5/time.xml"];

    // Each game's sky, read once, until its archives change.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<Rendering.SkyPath>> Skies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sun's and moon's paths across the game's sky, from its <c>time.xml</c>, read off the UI thread once per game;
    /// the game's own paths where no file is found or it can't be read.
    /// </summary>
    internal static Task<Rendering.SkyPath> SkyOf(LoadedGame game)
    {
        string Stamp(string archive)
        {
            string path = Path.Combine(game.Root, archive);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        }
        string key = $"{game.Root}|{game.Target}|{Stamp("mods/update/update.rpf")}|{Stamp("update/update.rpf")}|{Stamp("common.rpf")}";
        return Skies.GetOrAdd(key, _ => Task.Run(() => ReadSky(game)));
    }

    private static Rendering.SkyPath ReadSky(LoadedGame game)
    {
        foreach (string path in TimeXmlPaths)
        {
            try
            {
                if (Rendering.SkyPath.Read(ReadGameFile(game, path)) is { } sky)
                    return sky;
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
            }
        }
        return Rendering.SkyPath.Game;
    }

    /// <summary>
    /// A drawable's embedded textures in the texture viewer: a .ydr as it opens from the explorer, edited and saved back
    /// into it; a .yft's or .ydd's as a dictionary of their own, to look at and save elsewhere, as their textures can't
    /// be written back yet.
    /// </summary>
    internal void OpenEmbeddedTextures(EntryRecord entry, byte[] data, Action<SavedDocument>? closedAfterSave = null)
    {
        if (entry.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            OpenTextureDictionary(entry, closedAfterSave);
            return;
        }
        OpenEmbeddedDictionary(entry.Name, data, closedAfterSave);
    }

    /// <summary>
    /// What opens a drawable's embedded textures from the 3D viewer. A .ydr in an RPF is read again where it lives each
    /// time, with a reader of its own, as a search result is: a save into the RPF, from the texture editor or the
    /// viewer, closes the archive the explorer read it from.
    /// </summary>
    private Action<Action<SavedDocument>> OpensTexturesOf(EntryRecord entry, byte[] data)
    {
        ArchiveEntryTarget? where = null;
        string source = "";
        if (Provider.InArchive && entry.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                where = Provider.GetArchiveEntryTarget(entry);
                source = Provider.EntrySourcePath(entry);
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                where = null;
            }
        }
        if (where is null)
            return saved => OpenEmbeddedTextures(entry, data, saved);
        return saved =>
        {
            try
            {
                // Replaced since by a bigger file, it is held to the limit it was opened under.
                (byte[]? read, long size) = ReadArchived(where, ModelSizeLimit);
                if (read is null)
                {
                    SetStatus($"{entry.Name} is too large to view ({Formatting.FormatSize(size)})");
                    return;
                }
                OpenHitDictionary(entry.Name, (read, source, where), saved);
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                SetStatus($"Could not open texture dictionary: {error.Message}");
            }
        };
    }

    /// <summary>
    /// An archived file read where it lives, with an archive of its own that is closed after, and its size; not read
    /// (null) when it is bigger than <paramref name="limit"/>.
    /// </summary>
    private static (byte[]? Data, long Size) ReadArchived(ArchiveEntryTarget where, long limit)
    {
        ArchiveNode root = ArchiveNode.OpenRoot(where.RootPath, where.Crypto);
        try
        {
            if (root.FindEntry(where.EntryPath) is not { Entry: FiveFury.Rpf.RpfFileEntry file })
                throw new FileNotFoundException($"{Path.GetFileName(where.EntryPath)} is no longer in {Path.GetFileName(where.RootPath)}");
            // Its size as the explorer lists it.
            long size = file.LogicalSize > 0 ? file.LogicalSize : file.StoredSize;
            return (size > limit ? null : file.ReadStandalone(), size);
        }
        finally
        {
            root.Close();
        }
    }

    /// <summary>A .yft's or .ydd's embedded textures as a dictionary of their own, in the texture viewer.</summary>
    private void OpenEmbeddedDictionary(string name, byte[] data, Action<SavedDocument>? closedAfterSave = null)
    {
        byte[] dictionary;
        try
        {
            (IReadOnlyList<FiveFury.Textures.Texture> textures, FiveFury.GameTarget target) = Rendering.ModelContent.EmbeddedTextures(name, data);
            var own = new FiveFury.Textures.TextureDictionary();
            own.Textures.AddRange(textures);
            dictionary = new FiveFury.Ytd.Ytd(own, target).Write();
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            SetStatus($"Could not open the textures of {name}: {error.Message}");
            return;
        }
        TextureViewer.OpenDictionary($"{name} textures", "", dictionary, closedAfterSave: closedAfterSave);
        SetStatus($"Opening the textures of {name}");
    }

    /// <summary>The game while one is open, with its keys; none for an RPF opened on its own.</summary>
    private LoadedGame? LoadedGameOfProvider() =>
        Provider.GamePath.Length > 0 && Provider.GameTarget is { } target && Provider.Crypto is { } crypto
            ? new LoadedGame(Provider.GamePath, target, crypto)
            : null;

    /// <summary>A file of the game by its path from the game folder, RPFs included, read with a reader of its own.</summary>
    private static byte[] ReadGameFile(LoadedGame game, string path)
    {
        string[] parts = path.Split('/');
        int first = Array.FindIndex(parts, part => part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
        if (first < 0 || first == parts.Length - 1)
            return File.ReadAllBytes(Path.Combine([game.Root, .. parts]));
        var reader = new RpfProvider();
        try
        {
            reader.ApplyLoadedGame(game);
            reader.OpenArchive(Path.Combine([game.Root, .. parts[..(first + 1)]]));
            EntryRecord record = reader.ArchiveEntryFromRoot(string.Join('/', parts[(first + 1)..]))
                ?? throw new FileNotFoundException($"{parts[^1]} is no longer in {parts[first]}");
            return reader.ReadEntryBytes(record);
        }
        finally
        {
            reader.Close();
        }
    }

    /// <summary>
    /// The texture dictionaries in the same folder that usually hold a drawable's textures when it doesn't embed them
    /// (<see cref="ModelFile.DictionaryNames"/>), in that order; none for other files, and those that can't be read
    /// are left out.
    /// </summary>
    private IReadOnlyList<byte[]> DictionariesBeside(EntryRecord entry)
    {
        var found = new List<byte[]>();
        List<EntryRecord> folder = FolderRecords();
        foreach (string wanted in ModelFile.DictionaryNames(entry.Name))
        {
            if (folder.FirstOrDefault(sibling => string.Equals(sibling.Name, wanted, StringComparison.OrdinalIgnoreCase))
                is not { } sibling || sibling.Size > ModelFile.MaxSize)
                continue;
            try
            {
                found.Add(Provider.ReadEntryBytes(sibling));
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
            }
        }
        return found;
    }

    /// <summary>
    /// The folder's other texture dictionaries, for a drawable's textures that neither it nor the dictionaries named
    /// for it hold, in name order; each is read only when the search reaches it, while the viewer reads the file in
    /// the background. The archive reader is the explorer's, so the read itself happens on the UI thread.
    /// </summary>
    // Not kept parsed (they have no key): they are read through the explorer's own copy of the archive, which can be
    // older than the file on disk; the game's lists of a model's dictionaries, read afresh, hold the ones models share.
    private IReadOnlyList<Rendering.NearbyDictionary> NearbyDictionaries(EntryRecord entry)
    {
        if (!ModelFile.IsDrawableName(entry.Name))
            return [];
        var named = ModelFile.DictionaryNames(entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        RpfProvider provider = Provider;
        return [.. FolderRecords()
            .Where(sibling => !sibling.IsDirectory && sibling.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) && !named.Contains(sibling.Name))
            .Select(sibling => new Rendering.NearbyDictionary(sibling.Name, sibling.Size,
                () => Dispatcher.UIThread.CheckAccess() ? provider.ReadEntryBytes(sibling) : Dispatcher.UIThread.Invoke(() => provider.ReadEntryBytes(sibling))))];
    }

    /// <summary>
    /// Everything in the open folder, whatever the folder filter shows, in name order: a model's texture dictionaries
    /// are found there even when the filter hides them, and searched in an order the sort doesn't change.
    /// </summary>
    private List<EntryRecord> FolderRecords()
    {
        try
        {
            return [.. Provider.Entries(currentPath).OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            return [];
        }
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
