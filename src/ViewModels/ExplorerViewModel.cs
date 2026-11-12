using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HikariHopper.Core;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

/// <summary>
/// One explorer session (a tab): the port of <c>bridge.py::ExplorerBridge</c>. State is only
/// touched on the UI thread; writes run one at a time on a background queue.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    /// <summary>Data format marking drags that started in the explorer itself.</summary>
    public const string EntryDragMime = "application/x-hikarihopper-entry-drag";

    private const string NoGameStatus = "No game loaded  ·  Configure game paths in Settings";

    // Property groups raised together, one per Qt notify signal of the bridge.
    private static readonly string[] WorkspaceProperties =
    [
        nameof(ArchiveName), nameof(ArchivePath), nameof(GameName), nameof(GamePath), nameof(ArchiveTabName),
        nameof(ArchiveRootName), nameof(TabTitle), nameof(ArchiveIsRoot), nameof(HasWorkspace), nameof(HasGame), nameof(HasArchive),
        nameof(InArchive), nameof(GameEdition),
    ];
    private static readonly string[] CurrentPathProperties = [nameof(CurrentPath), nameof(CanGoUp)];
    private static readonly string[] PathPartsProperties =
        [nameof(PathParts), nameof(ArchiveGamePathParts), nameof(NavigationPathSegments), nameof(TabTitle)];
    private static readonly string[] CountsProperties =
        [nameof(TotalCount), nameof(VisibleCount), nameof(CanGoBack), nameof(CanGoForward)];
    private static readonly string[] SelectionProperties =
    [
        nameof(SelectedName), nameof(SelectedSize), nameof(SelectedIndex), nameof(HasSelection),
        nameof(SelectionCount), nameof(SelectionDeletable),
    ];
    private static readonly string[] SortProperties = [nameof(SortColumn), nameof(SortAscending)];
    private static readonly string[] GameLoadingProperties = [nameof(GameLoading), nameof(GameLoadingName), nameof(GameLoadingStage)];
    private static readonly string[] TreeFocusProperties = [nameof(TreeFocusIndex), nameof(TreeFocusPath)];

    private readonly ItemCollection<EntryItem> entries = [];
    private readonly ItemCollection<TreeItem> tree = [];
    private readonly HashSet<int> modelSelectedRows = [];
    private bool gameLoading;
    private bool gameIndexing;
    private string gameLoadingName = "";
    private string currentPath = ".";
    private List<string> history = [];
    private int historyIndex = -1;
    private string search = "";
    private string sortColumn = "name";
    private bool sortAscending = true;
    private string status = NoGameStatus;
    private string[] currentEntryNames = [];
    private int totalCount;
    private int visibleCount;
    private bool foldersVisible = true;
    private string viewMode = "list";
    private bool tabActive;

    public ExplorerViewModel()
    {
        TextureViewer = new TextureViewerViewModel();
        TextureViewer.SourceWriteFinished += OnTextureSourceWriteFinished;
        TextureViewer.PropertyChanged += OnTextureViewerPropertyChanged;
        TextureViewer.OpenDeclined += (_, message) => SetStatus(message);
        archiveProbe.Found += OnArchiveProbeFound;
    }

    /// <summary>Loads a game folder's keys; tests replace it so fake installations open without keys.</summary>
    internal static Func<string, LoadedGame> GameLoader { get; set; } = RpfProvider.LoadGame;

    /// <summary>A game was opened; the argument is its root folder.</summary>
    public event EventHandler<string>? GameOpened;

    /// <summary>A text file was opened from the list, for the text viewer to show and edit.</summary>
    public event EventHandler<TextFile>? TextFileOpened;

    /// <summary>An image file was opened for viewing; the image viewer shows it.</summary>
    public event EventHandler<ImageViewerViewModel>? ImageFileOpened;

    /// <summary>A collision, navmesh or drawable file was opened for viewing; the 3D viewer shows it.</summary>
    public event EventHandler<ModelFile>? ModelFileOpened;

    /// <summary>
    /// Whether the 3D viewer opens a file of this name: collisions (.ybn), navmeshes (.ynv), drawables (.ydr),
    /// drawable dictionaries (.ydd) and fragments (.yft).
    /// </summary>
    public static bool IsModelName(string name) =>
        name.EndsWith(".ybn", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ynv", StringComparison.OrdinalIgnoreCase)
        || ModelFile.IsDrawableName(name);

    /// <summary>
    /// Files changed at a location (an <see cref="EntryCreationTarget"/> or <see cref="EntryDeletionTarget"/>),
    /// so peer tabs refresh.
    /// </summary>
    public event EventHandler<object>? ContentChanged;

    /// <summary>Loose paths are about to be moved to the Recycle Bin, so peers release their handles.</summary>
    public event EventHandler<IReadOnlyList<string>>? LooseFilesAboutToBeDeleted;

    /// <summary>The root RPF at this path is about to be rewritten, so peers close their handles.</summary>
    public event EventHandler<string>? ArchiveWriteRequested;

    /// <summary>The view should ask the user to confirm <see cref="DeleteSelectedFiles"/>.</summary>
    public event EventHandler? DeleteConfirmationRequested;

    /// <summary>The view should focus the search field.</summary>
    public event EventHandler? SearchFocusRequested;

    public RpfProvider Provider { get; } = new();

    public ObservableCollection<EntryItem> EntriesModel => entries;

    public ObservableCollection<TreeItem> TreeModel => tree;

    public TextureViewerViewModel TextureViewer { get; }

    /// <summary>Whether this is the tab shown by <see cref="ExplorerTabsViewModel"/>.</summary>
    public bool TabActive
    {
        get => tabActive;
        internal set => SetProperty(ref tabActive, value);
    }

    public string ArchiveName => Provider.Info.Name;

    public string ArchivePath => Provider.Info.SourcePath;

    public string GameName => Provider.GameName;

    /// <summary>The open game's edition ("GTA V Legacy" or "GTA V Enhanced"), "" without a game.</summary>
    public string GameEdition => Provider.GameEdition;

    public string GamePath => Provider.GamePath;

    public string ArchiveTabName => Provider.RootArchiveName;

    public string ArchiveRootName => Provider.RootArchiveName;

    /// <summary>What a game's root is called: where its tab starts, and where the path bar's home mark goes.</summary>
    public const string HomeTitle = "Home";

    /// <summary>
    /// The folder or RPF the tab is in, as a file manager's tab reads. At a game's root it is
    /// <see cref="HomeTitle"/>, since the tab's mark already names the game; at a lone RPF's root, the RPF.
    /// </summary>
    public string TabTitle =>
        searching ? SearchTabTitle
        : NavigationPathSegments is [.., var current] ? current.Label
        : ArchiveIsRoot ? Provider.RootArchiveName : Provider.HasGame ? HomeTitle : "New tab";

    /// <summary>
    /// Whether the open RPF is the tab's root: opened on its own, from outside any game the tab has loaded.
    /// The tree, the tab's title and the home mark then follow the RPF, until it is closed.
    /// </summary>
    public bool ArchiveIsRoot => Provider.HasArchive && Provider.ArchiveGamePath.Length == 0;

    public bool HasWorkspace => Provider.IsOpen;

    public bool HasGame => Provider.HasGame;

    public bool HasArchive => Provider.HasArchive;

    public bool InArchive => Provider.InArchive;

    public string CurrentPath => currentPath;

    public IReadOnlyList<string> PathParts => currentPath == "." ? [] : currentPath.Split('/');

    public IReadOnlyList<string> ArchiveGamePathParts
    {
        get
        {
            var parts = Provider.ArchiveGamePath.Length > 0 ? Provider.ArchiveGamePath.Split('/').ToList() : [];
            if (Provider.ArchivePrefix.Length > 0)
                parts.AddRange(Provider.ArchivePrefix.Split('/'));
            return parts;
        }
    }

    /// <summary>Breadcrumb segments from the game root (or root RPF) to the current folder.</summary>
    public IReadOnlyList<PathSegment> NavigationPathSegments
    {
        get
        {
            var segments = new List<PathSegment>();
            if (!Provider.IsOpen)
                return segments;

            string[] currentParts = currentPath == "." ? [] : currentPath.Split('/');
            if (Provider.InArchive)
            {
                string[] gameParts = Provider.ArchiveGamePath.Length > 0 ? Provider.ArchiveGamePath.Split('/') : [];
                for (int index = 0; index < gameParts.Length; index++)
                {
                    string path = string.Join('/', gameParts[..(index + 1)]);
                    bool isArchive = index == gameParts.Length - 1;
                    segments.Add(new PathSegment(gameParts[index], isArchive ? $"rpf://{path}" : $"game://{path}", isArchive));
                }

                var nestedPrefixes = Provider.ArchiveChainPrefixes.Skip(1).ToHashSet(StringComparer.Ordinal);
                string[] prefixParts = Provider.ArchivePrefix.Length > 0 ? Provider.ArchivePrefix.Split('/') : [];
                for (int index = 0; index < prefixParts.Length; index++)
                {
                    string fullPath = string.Join('/', prefixParts[..(index + 1)]);
                    bool isArchive = nestedPrefixes.Contains(fullPath);
                    segments.Add(new PathSegment(prefixParts[index],
                        isArchive ? NestedArchiveKey(fullPath) : ArchiveDirectoryKey(fullPath), isArchive));
                }

                for (int index = 0; index < currentParts.Length; index++)
                {
                    string localPath = string.Join('/', currentParts[..(index + 1)]);
                    string fullPath = string.Join('/', new[] { Provider.ArchivePrefix, localPath }.Where(part => part.Length > 0));
                    segments.Add(new PathSegment(currentParts[index], ArchiveDirectoryKey(fullPath), false));
                }
                return segments;
            }

            for (int index = 0; index < currentParts.Length; index++)
            {
                string path = string.Join('/', currentParts[..(index + 1)]);
                segments.Add(new PathSegment(currentParts[index], $"game://{path}", false));
            }
            return segments;
        }
    }

    /// <summary>The live filter text, so the table can mark the matched characters.</summary>
    public string SearchQuery => search;

    /// <summary><c>name</c>, <c>type</c> or <c>size</c>.</summary>
    public string SortColumn => sortColumn;

    public bool SortAscending => sortAscending;

    public string Status => status;

    public int TotalCount => totalCount;

    public int VisibleCount => visibleCount;

    public bool CanGoBack => searching || historyIndex > 0;

    public bool CanGoForward => historyIndex + 1 < history.Count;

    public bool CanGoUp => searching || currentPath is not ("" or ".");

    public bool FoldersVisible => foldersVisible;

    public bool GameLoading => gameLoading;

    public string GameLoadingName => gameLoadingName;

    /// <summary>What the game load is doing, for its loading screen: its keys, then, when the game's archives changed
    /// since the last time, the textures its models take from elsewhere (<see cref="GameTextures"/>).</summary>
    public string GameLoadingStage => gameIndexing ? "INDEXING TEXTURES" : "LOADING GAME";

    public string EntryDragMimeType => EntryDragMime;

    /// <summary><c>list</c> or <c>grid</c>.</summary>
    public string ViewMode => viewMode;

    /// <summary>The view the entries are shown in: <see cref="ViewMode"/>, except that results are always a list.</summary>
    public string ShownViewMode => HasSearchQuery ? "list" : viewMode;

    /// <summary>Whether the view can be chosen: not while results are shown, which only the list has room for.</summary>
    public bool CanChooseViewMode => !HasSearchQuery;

    private void RaiseAll(string[] names)
    {
        foreach (string name in names)
            OnPropertyChanged(name);
    }

    private void SetStatus(string value)
    {
        status = value;
        OnPropertyChanged(nameof(Status));
    }

    private void ResetNavigation(string path = ".")
    {
        string normalized = path.Length > 0 ? path : ".";
        history = [normalized];
        historyIndex = 0;
        search = "";
        currentPath = normalized;
        OnPropertyChanged(nameof(SearchQuery));
    }

    private void RefreshEntries()
    {
        if (SearchShowsLocation)
        {
            RefreshSearchEntries();
            return;
        }
        string current = currentPath;
        while (current != "." && Provider.IsOpen && !Provider.HasDirectory(current))
            current = PosixParent(current);
        if (current != currentPath)
        {
            ResetNavigation(current);
            RaiseAll(CurrentPathProperties);
            RaiseAll(PathPartsProperties);
        }
        List<EntryRecord> records = Provider.Entries(currentPath);
        currentEntryNames = records.Select(entry => entry.Name).ToArray();
        totalCount = records.Count;
        string query = CaseFold(search).Trim();
        var visible = SortedEntries(records
            .Where(entry => searching ? PassesFolderSearch(entry) : query.Length == 0 || CaseFold(entry.Name).Contains(query, StringComparison.Ordinal))
            .ToList());
        SetEntries(visible);
        RefreshTree();
        visibleCount = visible.Count;
        RaiseAll(CountsProperties);
    }

    private List<EntryRecord> SortedEntries(List<EntryRecord> records)
    {
        static int ByName(EntryRecord left, EntryRecord right) =>
            string.CompareOrdinal(CaseFold(left.Name), CaseFold(right.Name));

        Comparison<EntryRecord> fileOrder = sortColumn switch
        {
            "type" => (left, right) =>
            {
                int kind = string.CompareOrdinal(CaseFold(left.Kind), CaseFold(right.Kind));
                return kind != 0 ? kind : ByName(left, right);
            },
            "size" => (left, right) =>
            {
                // By the size shown, so the order matches the column.
                int size = left.ShownSize.CompareTo(right.ShownSize);
                return size != 0 ? size : ByName(left, right);
            },
            // Search results only: where they live, then their names.
            "location" => (left, right) =>
            {
                int location = string.CompareOrdinal(CaseFold(left.Path[..Math.Max(0, left.Path.Length - left.Name.Length)]),
                    CaseFold(right.Path[..Math.Max(0, right.Path.Length - right.Name.Length)]));
                return location != 0 ? location : ByName(left, right);
            },
            _ => ByName,
        };

        // Folders always sort by name; LINQ ordering is stable like Python's sorted(reverse=...).
        IEnumerable<EntryRecord> Order(IEnumerable<EntryRecord> items, Comparison<EntryRecord> comparison)
        {
            var comparer = Comparer<EntryRecord>.Create(comparison);
            return sortAscending
                ? items.OrderBy(item => item, comparer)
                : items.OrderByDescending(item => item, comparer);
        }

        return Order(records.Where(entry => entry.IsDirectory), ByName)
            .Concat(Order(records.Where(entry => !entry.IsDirectory), fileOrder))
            .ToList();
    }

    private void SetEntries(IEnumerable<EntryRecord> records)
    {
        modelSelectedRows.Clear();
        entries.ResetItems(records.Select(record => new EntryItem(record)));
    }

    private EntryRecord? EntryAt(int row) => row >= 0 && row < entries.Count ? entries[row].Record : null;

    private void PublishNavigation()
    {
        ClearSelection();
        RaiseAll(CurrentPathProperties);
        RaiseAll(PathPartsProperties);
        RaiseAll(CountsProperties);
        RefreshEntries();
    }

    private void PublishWorkspace()
    {
        // Another game or archive: whatever was being searched is not there any more.
        EndSearch();
        RaiseAll(WorkspaceProperties);
        PublishNavigation();
    }

    /// <summary>Opens the configured game synchronously (startup restore), unless Settings turned that off.</summary>
    public void RestoreLastGame()
    {
        string path = GameSettings.GameRootToRestore();
        if (path.Length > 0)
            OpenGame(path);
    }

    /// <summary>Opens the configured installation of <paramref name="edition"/> (any edition when empty).</summary>
    public void OpenConfiguredGame(string edition = "")
    {
        string path = ConfiguredGamePath(edition);
        if (path.Length == 0)
        {
            string editionName = edition switch
            {
                GameSettings.EnhancedEdition => "GTA V Enhanced",
                GameSettings.LegacyEdition => "GTA V Legacy",
                _ => "game",
            };
            SetStatus($"Configure the {editionName} path in Settings");
            return;
        }
        OpenGameAsync(path);
    }

    private static string ConfiguredGamePath(string edition = "") => GameSettings.ConfiguredGameRootToOpen(edition);

    public void OpenGame(string path)
    {
        if (EntryOperationBusy)
            return;
        SetStatus("Loading game keys…");
        try
        {
            Provider.ApplyLoadedGame(GameLoader(path));
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not load game: {error.Message}");
            return;
        }
        PublishOpenedGame();
    }

    /// <summary>Loads the game's keys in the background; false while another operation runs.</summary>
    public bool OpenGameAsync(string path)
    {
        if (EntryOperationBusy)
            return false;
        string edition = GameSettings.GameEditionForPath(path);
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        gameLoadingName = edition switch
        {
            GameSettings.EnhancedEdition => "Grand Theft Auto V Enhanced",
            GameSettings.LegacyEdition => "Grand Theft Auto V Legacy",
            _ => folderName.Length > 0 ? folderName : "Game",
        };
        SetGameLoading(true);
        SetStatus($"Loading {gameLoadingName}…");
        Func<string, LoadedGame> loader = GameLoader;
        Enqueue(() =>
        {
            LoadedGame game = loader(path);
            ReadGameTextures(game);
            return game;
        }, GameLoadCompleted, GameLoadFailed);
        return true;
    }

    /// <summary>
    /// Reads where the game keeps models' textures beyond the models themselves (<see cref="GameTextures"/>) as part of
    /// loading it, so a model opened afterwards has them from its first frame: from the cache in a moment, or, when the
    /// game's archives changed, from the whole game, which the loading screen says. Tables that can't be made don't stop
    /// the game from loading; its models open without them.
    /// </summary>
    private void ReadGameTextures(LoadedGame game)
    {
        try
        {
            GameTextures.ForGame(game).Load(() => Dispatcher.UIThread.Post(() =>
            {
                if (!gameLoading || gameIndexing)
                    return;
                gameIndexing = true;
                RaiseAll(GameLoadingProperties);
                SetStatus($"Indexing the textures of {gameLoadingName}…");
            })).Wait();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }
    }

    private void GameLoadCompleted(object? result)
    {
        if (result is not LoadedGame loaded)
        {
            GameLoadFailed("Invalid game load result");
            return;
        }
        try
        {
            Provider.ApplyLoadedGame(loaded);
            PublishOpenedGame();
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not load game: {error.Message}");
        }
        SetGameLoading(false);
    }

    private void GameLoadFailed(string message)
    {
        SetStatus($"Could not load game: {message}");
        SetGameLoading(false);
    }

    private void SetGameLoading(bool loading)
    {
        if (loading == gameLoading)
            return;
        gameLoading = loading;
        if (!loading)
        {
            gameLoadingName = "";
            gameIndexing = false;
        }
        RaiseAll(GameLoadingProperties);
        RaiseEntryOperationStateChanged();
    }

    private void PublishOpenedGame()
    {
        GameSettings.RememberGameRoot(Provider.GamePath);
        // Where the game keeps models' textures beyond the models themselves: read while the game loaded, unless it was
        // opened without the loading screen; then in the background now, so the 3D viewer has it when a model opens.
        if (LoadedGameOfProvider() is { } game)
            _ = GameTextures.ForGame(game).Load();
        GameOpened?.Invoke(this, Provider.GamePath);
        expandedNodes = new HashSet<string>(StringComparer.Ordinal) { "game://." };
        ResetNavigation();
        PublishWorkspace();
        SetStatus($"Loaded {Provider.Info.Version}");
    }

    /// <summary>Asks for an RPF with the native picker and opens it.</summary>
    public async Task OpenArchiveDialog()
    {
        IReadOnlyList<string> paths = await AppServices.Dialogs.PickOpenFilesAsync(
            "Open RPF Archive", [new FileFilter("RPF archives", ["*.rpf"])], startPath: Provider.GamePath);
        if (paths.Count > 0 && paths[0].Length > 0)
            OpenArchive(paths[0]);
    }

    /// <summary>Asks for a folder to pack as an RPF; suggests <c>&lt;folder&gt;.rpf</c>.</summary>
    public async Task<RpfSource> ChooseRpfFolderSource()
    {
        string? path = await AppServices.Dialogs.PickFolderAsync("Choose a folder to pack as RPF", Provider.GamePath);
        return RpfSourceResult(path, folder: true);
    }

    /// <summary>Asks for a ZIP to pack as an RPF; suggests <c>&lt;zip stem&gt;.rpf</c>.</summary>
    public async Task<RpfSource> ChooseRpfZipSource()
    {
        IReadOnlyList<string> paths = await AppServices.Dialogs.PickOpenFilesAsync(
            "Choose a ZIP archive to pack as RPF", [new FileFilter("ZIP archives", ["*.zip"])],
            startPath: Provider.GamePath);
        return RpfSourceResult(paths.Count > 0 ? paths[0] : null, folder: false);
    }

    internal static RpfSource RpfSourceResult(string? path, bool folder)
    {
        if (string.IsNullOrEmpty(path))
            return new RpfSource("", "");
        string source = path.Replace('/', '\\');
        string trimmed = Path.TrimEndingDirectorySeparator(source);
        string stem = folder ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed);
        return new RpfSource(source, $"{stem}.rpf");
    }

    public void OpenArchive(string path)
    {
        if (EntryOperationBusy)
            return;
        try
        {
            Provider.OpenArchive(path);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open archive: {error.Message}");
            return;
        }
        ClearArchiveExpansion();
        ResetNavigation();
        ExpandCurrentBranch();
        PublishWorkspace();
        SetStatus($"Opened {ArchiveName}");
    }

    public void ShowGame()
    {
        if (EntryOperationBusy)
            return;
        try
        {
            Provider.ShowGame();
        }
        catch (ArgumentException error)
        {
            SetStatus(error.Message);
            return;
        }
        ResetNavigation();
        ExpandCurrentBranch();
        PublishWorkspace();
        SetStatus($"Browsing {Provider.GameName}");
    }

    public void ShowArchive()
    {
        if (EntryOperationBusy)
            return;
        try
        {
            Provider.ShowArchive();
        }
        catch (ArgumentException error)
        {
            SetStatus(error.Message);
            return;
        }
        ResetNavigation();
        ExpandCurrentBranch();
        PublishWorkspace();
        SetStatus($"Browsing {Provider.ArchiveName}");
    }

    public void CloseArchive()
    {
        if (EntryOperationBusy)
            return;
        Provider.CloseArchive();
        ClearArchiveExpansion();
        if (Provider.HasGame)
        {
            ResetNavigation();
            PublishWorkspace();
            SetStatus($"Browsing {Provider.GameName}");
        }
        else
        {
            CloseWorkspace();
        }
    }

    public void CloseWorkspace()
    {
        if (EntryOperationBusy)
            return;
        Provider.Close();
        SetEntries([]);
        SetTreeRows([]);
        history = [];
        historyIndex = -1;
        search = "";
        currentPath = ".";
        expandedNodes.Clear();
        UpdateTreeFocus("", -1);
        RaiseAll(WorkspaceProperties);
        RaiseAll(CurrentPathProperties);
        RaiseAll(PathPartsProperties);
        ClearSelection();
        totalCount = 0;
        visibleCount = 0;
        RaiseAll(CountsProperties);
        SetStatus(NoGameStatus);
    }

    public void Navigate(string path)
    {
        if (EntryOperationBusy)
            return;
        if (searching)
            EndSearch();
        string normalized = path.Length > 0 ? path : ".";
        if (normalized == currentPath)
            return;
        history = history.Take(historyIndex + 1).ToList();
        history.Add(normalized);
        historyIndex++;
        currentPath = normalized;
        ExpandCurrentBranch();
        PublishNavigation();
    }

    public void GoBack()
    {
        if (EntryOperationBusy)
            return;
        // Back from a search is the folder it started from.
        if (searching)
        {
            EndSearch();
            return;
        }
        if (historyIndex > 0)
        {
            historyIndex--;
            currentPath = history[historyIndex];
            ExpandCurrentBranch();
            PublishNavigation();
        }
    }

    public void GoForward()
    {
        if (EntryOperationBusy)
            return;
        if (historyIndex + 1 < history.Count)
        {
            historyIndex++;
            currentPath = history[historyIndex];
            ExpandCurrentBranch();
            PublishNavigation();
        }
    }

    public void GoUp()
    {
        if (searching)
        {
            EndSearch();
            return;
        }
        if (CanGoUp)
            Navigate(PosixParent(currentPath));
    }

    public void SetSearch(string value)
    {
        if (EntryOperationBusy)
            return;
        if (value == search)
            return;
        search = value;
        OnPropertyChanged(nameof(SearchQuery));
        ClearSelection();
        RefreshEntries();
    }

    /// <summary>Sorts by <c>name</c>, <c>type</c> or <c>size</c>; the same column again reverses the order.</summary>
    public void SortEntries(string column)
    {
        if (EntryOperationBusy)
            return;
        string normalized = CaseFold(column);
        if (normalized is not ("name" or "type" or "size") && !(searching && normalized == "location"))
            return;

        var selectedPaths = SelectedEntries().Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        string currentEntryPath = EntryAt(selectedIndex)?.Path ?? "";
        string anchorPath = EntryAt(selectionAnchor)?.Path ?? "";

        if (normalized == sortColumn)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = normalized;
            sortAscending = true;
        }
        RaiseAll(SortProperties);
        RefreshEntries();

        var rowsByPath = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int row = 0; row < entries.Count; row++)
            rowsByPath[entries[row].Path] = row;
        var rows = selectedPaths.Where(rowsByPath.ContainsKey).Select(path => rowsByPath[path]).ToHashSet();
        if (rows.Count > 0)
        {
            SetSelection(rows,
                rowsByPath.GetValueOrDefault(currentEntryPath, rows.Min()),
                rowsByPath.GetValueOrDefault(anchorPath, rows.Min()),
                force: true);
        }
    }

    /// <summary>Re-reads the current folder (the toolbar refresh).</summary>
    public void Refresh()
    {
        if (EntryOperationBusy)
            return;
        if (SearchShowsLocation && searchIndex is not null)
        {
            searchIndexTask = searchIndex.Refresh();
            RunSearch(immediately: true);
            return;
        }
        if (Provider.IsOpen)
        {
            ClearSelection();
            RefreshEntries();
            SetStatus($"Refreshed {ArchiveName}");
        }
    }

    public void SetFoldersVisible(bool visible)
    {
        if (visible == foldersVisible)
            return;
        foldersVisible = visible;
        OnPropertyChanged(nameof(FoldersVisible));
    }

    /// <summary>Switches between <c>list</c> and <c>grid</c>; other values are ignored.</summary>
    public void SetViewMode(string mode)
    {
        string normalized = CaseFold(mode);
        if (normalized is not ("list" or "grid") || normalized == viewMode || !CanChooseViewMode)
            return;
        viewMode = normalized;
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(ShownViewMode));
    }

    public void RequestSearchFocus() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    private static string CaseFold(string value) => value.ToLowerInvariant();

    /// <summary><c>PurePosixPath(path).parent</c>: <c>"a/b"</c> → <c>"a"</c>, <c>"a"</c> → <c>"."</c>.</summary>
    private static string PosixParent(string path)
    {
        string trimmed = path.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 ? "." : slash == 0 ? "/" : trimmed[..slash];
    }
}
