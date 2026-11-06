using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Core.Search;
using HikariHopper.Infrastructure;

namespace HikariHopper.ViewModels;

/// <summary>Where a search looks.</summary>
public enum SearchScope
{
    /// <summary>The folder shown; with <see cref="ExplorerViewModel.SearchSubfolders"/>, everything under it too.</summary>
    Folder,

    /// <summary>The RPF shown, nested ones in it included.</summary>
    Archive,

    /// <summary>The whole game, or the whole RPF in a tab that opened one on its own.</summary>
    Everywhere,
}

/// <summary>
/// Searching: the folder shown, or more of the game, in the same search row and with the same matching. The folder
/// alone is its own listing, filtered; anything wider is files found in the game's index, each of which knows where
/// it lives, so opening, copying, extracting, renaming and deleting it work wherever that is. Either way the list, its
/// selection and its commands are the explorer's own.
/// </summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>How long typing pauses before a search runs; tests set it to nothing.</summary>
    internal static int SearchDelay { get; set; } = 120;

    /// <summary>The most results a search lists.</summary>
    internal const int SearchLimit = 100_000;

    private static readonly string[] SearchProperties =
    [
        nameof(IsSearching), nameof(SearchScopeValue), nameof(SearchText), nameof(SearchMatchCase), nameof(SearchWildcards),
        nameof(SearchRegex), nameof(SearchError), nameof(SearchHitCount), nameof(SearchIndexing), nameof(SearchIndexProgress),
        nameof(SearchIndexText), nameof(TabTitle), nameof(CanPaste), nameof(CanGoBack), nameof(CanGoUp), nameof(ShowsItemCount),
        nameof(CanSearchArchive), nameof(SearchSubfolders), nameof(SearchShowsLocation),
    ];

    private bool searching;
    private SearchScope searchScope = SearchScope.Everywhere;
    private string searchText = "";
    private bool searchMatchCase;
    private bool? searchWildcards;
    private bool searchRegex;
    private bool searchSubfolders;
    private SearchPattern? folderPattern;
    private string searchError = "";
    private string searchPrefix = "";
    private List<SearchHit> searchHits = [];
    private SearchIndex? searchIndex;
    private CancellationTokenSource? searchRun;
    private Task searchTask = Task.CompletedTask;
    private Task searchIndexTask = Task.CompletedTask;
    private long lastIndexRerun;
    private bool searchedComplete;

    /// <summary>Whether the tab shows search results instead of a folder.</summary>
    public bool IsSearching => searching;

    /// <summary>Whether the status bar counts the folder's items: not while searching, where the tab counts results.</summary>
    public bool ShowsItemCount => HasWorkspace && !searching;

    public SearchScope SearchScopeValue => searchScope;

    public string SearchText => searchText;

    public bool SearchMatchCase => searchMatchCase;

    /// <summary>Whether the text is read as wildcards: switched on, or on by itself for a * or ?.</summary>
    public bool SearchWildcards => !searchRegex && (searchWildcards ?? SearchPattern.LooksLikeWildcards(searchText));

    public bool SearchRegex => searchRegex;

    /// <summary>Whether a search of the folder takes in everything under it as well.</summary>
    public bool SearchSubfolders => searchSubfolders;

    /// <summary>
    /// Whether the results are files found in the index, from all over (each shown with where it lives), rather than
    /// the folder's own listing, filtered.
    /// </summary>
    public bool SearchShowsLocation => searching && (searchScope != SearchScope.Folder || searchSubfolders);

    /// <summary>Why the text can't be searched (a regular expression that doesn't parse); empty when it can.</summary>
    public string SearchError => searchError;

    public int SearchHitCount => SearchShowsLocation ? searchHits.Count : entries.Count;

    /// <summary>Whether the index is still being built, so more results may come.</summary>
    public bool SearchIndexing => SearchShowsLocation && searchIndex is { State.Complete: false };

    public double SearchIndexProgress => searchIndex?.State is { ArchivesTotal: > 0 } state ? (double)state.ArchivesDone / state.ArchivesTotal : 0;

    public string SearchIndexText => searchIndex?.State is { } state
        ? state.ArchivesTotal == 0 ? "Indexing…" : $"Indexing {state.ArchivesDone:N0} of {state.ArchivesTotal:N0} archives"
        : "";

    /// <summary>Whether "This RPF" can be searched: an RPF is shown.</summary>
    public bool CanSearchArchive => Provider.InArchive;

    /// <summary>The search in flight, for tests to wait on.</summary>
    internal Task SearchSettled => searchTask;

    /// <summary>The index refresh in flight, for tests to wait on.</summary>
    internal Task SearchIndexSettled => searchIndexTask;

    /// <summary>The tab's title while searching: how many files were found.</summary>
    private string SearchTabTitle
    {
        get
        {
            if (!SearchShowsLocation)
            {
                // The folder's own listing: its folders and files.
                int shown = searchError.Length > 0 ? 0 : entries.Count;
                return shown == 0 ? "No items" : shown == 1 ? "1 item" : $"{shown:N0} items";
            }
            int count = searchHits.Count;
            if (searchError.Length > 0 || searchText.Length == 0)
                return searchText.Length == 0 ? "Search" : "No files";
            if (count == 0)
                return SearchIndexing || !searchedComplete ? "Searching…" : "No files";
            string number = count >= SearchLimit ? $"{SearchLimit:N0}+" : $"{count:N0}";
            return $"{number} {(count == 1 ? "file" : "files")}{(SearchIndexing ? "…" : "")}";
        }
    }

    /// <summary>Starts searching <paramref name="scope"/> (the whole game by default), or changes what is searched while one runs.</summary>
    public void BeginSearch(SearchScope scope = SearchScope.Everywhere)
    {
        if (!HasWorkspace)
            return;
        if (scope == SearchScope.Archive && !Provider.InArchive)
            scope = SearchScope.Everywhere;
        searchScope = scope;
        if (!searching)
        {
            searching = true;
            // A search starts afresh, from what the folder's box held if anything; that filter is done with.
            searchText = search;
            search = "";
            OnPropertyChanged(nameof(SearchQuery));
        }
        ClearSelection();
        UseIndexIfNeeded();
        RunSearch(immediately: true);
        RaiseAll(SearchProperties);
        RaiseAll(CountsProperties);
    }

    /// <summary>The folder alone, or everything under it too.</summary>
    public void ToggleSearchSubfolders()
    {
        searchSubfolders = !searchSubfolders;
        if (!searching)
            return;
        ClearSelection();
        UseIndexIfNeeded();
        RunSearch(immediately: true);
        RaiseAll(SearchProperties);
    }

    /// <summary>Takes the index the scope needs, starting its refresh, or lets it go when the folder's listing will do.</summary>
    private void UseIndexIfNeeded()
    {
        SearchIndex? index = SearchShowsLocation ? IndexFor(searchScope) : null;
        if (searchIndex != index)
        {
            if (searchIndex is not null)
                searchIndex.Changed -= OnSearchIndexChanged;
            searchIndex = index;
            if (searchIndex is not null)
                searchIndex.Changed += OnSearchIndexChanged;
        }
        searchHits = [];
        searchPrefix = ScopePrefix(searchScope);
        if (searchIndex is not null)
            searchIndexTask = searchIndex.Refresh();
    }

    /// <summary>Back to the folder the search started from; the search is kept for the next one.</summary>
    public void EndSearch()
    {
        if (!searching)
            return;
        searching = false;
        searchRun?.Cancel();
        if (searchIndex is not null)
            searchIndex.Changed -= OnSearchIndexChanged;
        searchIndex = null;
        searchHits = [];
        folderPattern = null;
        appliedSearchKey = "";
        ClearSelection();
        RefreshEntries();
        RaiseAll(SearchProperties);
        RaiseAll(CountsProperties);
    }

    public void SetSearchText(string text)
    {
        if (text == searchText)
            return;
        searchText = text;
        RunSearch(immediately: false);
        RaiseAll(SearchProperties);
    }

    public void ToggleSearchMatchCase()
    {
        searchMatchCase = !searchMatchCase;
        RunSearch(immediately: true);
        RaiseAll(SearchProperties);
    }

    /// <summary>Wildcards on or off, against what the text would choose by itself.</summary>
    public void ToggleSearchWildcards()
    {
        bool on = !SearchWildcards;
        searchWildcards = on == SearchPattern.LooksLikeWildcards(searchText) ? null : on;
        if (on)
            searchRegex = false;
        RunSearch(immediately: true);
        RaiseAll(SearchProperties);
    }

    public void ToggleSearchRegex()
    {
        searchRegex = !searchRegex;
        RunSearch(immediately: true);
        RaiseAll(SearchProperties);
    }

    /// <summary>
    /// The index a scope needs: the game's, or, for an RPF that isn't part of the game shown, that RPF's own.
    /// </summary>
    private SearchIndex? IndexFor(SearchScope scope)
    {
        bool outsideGame = Provider.InArchive && Provider.ArchiveGamePath.Length == 0;
        if (Provider.HasGame && !(outsideGame && scope != SearchScope.Everywhere))
            return SearchIndex.ForGame(Provider.GamePath, Provider.Crypto);
        if (Provider.HasArchive && Provider.ArchivePath.Length > 0)
            return SearchIndex.ForArchive(Provider.ArchivePath, Provider.Crypto);
        return null;
    }

    /// <summary>Where a scope's results must be under, as a path of the index; empty for all of it.</summary>
    private string ScopePrefix(SearchScope scope) => scope switch
    {
        SearchScope.Folder => AddressText.Replace('\\', '/'),
        SearchScope.Archive => string.Join('/', new[]
        {
            Provider.ArchiveGamePath.Length > 0 ? Provider.ArchiveGamePath : Path.GetFileName(Provider.ArchivePath),
            Provider.ArchivePrefix,
        }.Where(part => part.Length > 0)),
        _ => "",
    };

    private void RunSearch(bool immediately)
    {
        searchRun?.Cancel();
        var run = new CancellationTokenSource();
        searchRun = run;
        SearchPattern? pattern = SearchPattern.Parse(searchText, searchMatchCase, searchWildcards, searchRegex, out string error);
        searchError = error;
        if (!SearchShowsLocation)
        {
            // The folder's own listing, filtered here and now: every name, with no text.
            folderPattern = pattern;
            searchedComplete = true;
            ClearSelection();
            RefreshEntries();
            RaiseAll(SearchProperties);
            return;
        }
        if (pattern is null || searchIndex is null)
        {
            ApplyHits([], complete: true);
            return;
        }
        SearchIndexState state = searchIndex.State;
        string prefix = searchPrefix;
        int delay = immediately ? 0 : SearchDelay;
        searchTask = Task.Run(async () =>
        {
            if (delay > 0)
                await Task.Delay(delay, run.Token).ConfigureAwait(false);
            return state.Search(pattern, prefix, SearchLimit, run.Token);
        }, run.Token).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
                Dispatcher.UIThread.Post(() =>
                {
                    if (searchRun == run && searching)
                        ApplyHits(task.Result, state.Complete);
                });
        }, TaskScheduler.Default);
    }

    /// <summary>Shows <paramref name="hits"/>, keeping the results that were selected selected.</summary>
    /// <summary>What was searched for, to tell a new search from the same one run again as the index changed.</summary>
    private string SearchKey() =>
        $"{searchText}\n{searchMatchCase}\n{searchWildcards}\n{searchRegex}\n{searchScope}\n{searchSubfolders}\n{searchPrefix}";

    private string appliedSearchKey = "";

    /// <summary>
    /// Whether the list is being refreshed in place: the same search run again, so the view keeps where it is
    /// scrolled to and the selection it gets back doesn't move it.
    /// </summary>
    internal bool KeepsListPosition { get; private set; }

    /// <summary>
    /// Shows <paramref name="hits"/>, keeping the results that were selected selected. The same search run again (the
    /// index filling in, or checked when a search starts) changes the list only if what it found changed, and then in
    /// place: the view stays where you scrolled it.
    /// </summary>
    private void ApplyHits(List<SearchHit> hits, bool complete)
    {
        string key = SearchKey();
        bool again = key == appliedSearchKey;
        appliedSearchKey = key;
        searchedComplete = complete;
        if (again && hits.Count == searchHits.Count && hits.Select(hit => hit.Path).SequenceEqual(searchHits.Select(hit => hit.Path), StringComparer.Ordinal))
        {
            RaiseAll(SearchProperties);
            return;
        }
        var selected = SelectedEntries().Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        searchHits = hits;
        KeepsListPosition = again;
        try
        {
            RefreshEntries();
            if (selected.Count > 0)
            {
                var rows = Enumerable.Range(0, entries.Count).Where(row => selected.Contains(entries[row].Path)).ToHashSet();
                if (rows.Count > 0)
                    SetSelection(rows, rows.Min(), rows.Min(), force: true);
            }
        }
        finally
        {
            KeepsListPosition = false;
        }
        RaiseAll(SearchProperties);
    }

    private void OnSearchIndexChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!searching || sender != searchIndex)
            return;
        RaiseAll(SearchProperties);
        // Results follow the index as it fills in, a couple of times a second, and once more when it is done.
        bool complete = searchIndex!.State.Complete;
        if (complete || Environment.TickCount64 - lastIndexRerun > 500)
        {
            lastIndexRerun = Environment.TickCount64;
            RunSearch(immediately: true);
        }
    });

    /// <summary>The results, as the explorer's rows: each one's record knows its hit.</summary>
    private void RefreshSearchEntries()
    {
        var records = SortedEntries([.. searchHits.Select(hit =>
            new EntryRecord(hit.Name, hit.Path, RpfProvider.FileKind(hit.Name), hit.Size, NativeEntry: hit))]);
        SetEntries(records);
        totalCount = visibleCount = records.Count;
        currentEntryNames = [];
        RaiseAll(CountsProperties);
    }

    private List<SearchHit> SelectedHits() => [.. SelectedEntries().Select(entry => entry.NativeEntry).OfType<SearchHit>()];

    /// <summary>The parts of a name the search matched, to mark them; none when it isn't searching the folder.</summary>
    public IReadOnlyList<(int Start, int Length)> SearchSpans(string name) =>
        searching && folderPattern?.Match(name) is { } spans ? spans : [];

    /// <summary>Whether a folder's entry passes the search of the folder.</summary>
    private bool PassesFolderSearch(EntryRecord entry) =>
        searchError.Length == 0 && (folderPattern is null || folderPattern.Match(entry.Name) is not null);

    // ---------------- Where a result lives ----------------

    /// <summary>
    /// A result's place: on disk, or in an RPF on disk (<see cref="RootFull"/>), through nested ones
    /// (<see cref="NestedPrefix"/>), in a folder inside the innermost (<see cref="InnerDirectory"/>).
    /// </summary>
    private sealed record HitPlace(SearchHit Hit, string RootFull, string NestedPrefix, string InnerDirectory, string LocalPath, string FromRoot, string LooseFull)
    {
        public bool InArchive => RootFull.Length > 0;
    }

    private HitPlace PlaceOf(SearchHit hit)
    {
        string[] parts = hit.Path.Split('/');
        int[] containers = [.. Enumerable.Range(0, parts.Length - 1).Where(i => parts[i].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))];
        bool standalone = searchIndex?.StandaloneArchive == true;
        if (containers.Length == 0)
            return new HitPlace(hit, "", "", "", hit.Path, "", Path.Combine([Provider.GamePath, .. parts]));
        int first = containers[0], last = containers[^1];
        string rootFull = standalone && first == 0 ? searchIndex!.Root : Path.Combine([Provider.GamePath, .. parts[..(first + 1)]]);
        string nested = string.Join('/', parts[(first + 1)..(last + 1)]);
        string inner = string.Join('/', parts[(last + 1)..^1]);
        return new HitPlace(hit, rootFull, nested, inner.Length == 0 ? "." : inner,
            string.Join('/', parts[(last + 1)..]), string.Join('/', parts[(first + 1)..]), "");
    }

    private EntryCreationTarget LocationOf(HitPlace place) => place.InArchive
        ? new EntryCreationTarget(ArchivePath: place.RootFull, ArchivePrefix: place.NestedPrefix, InternalDirectory: place.InnerDirectory,
            Game: Provider.GameTarget, Crypto: Provider.Crypto ?? GameKeys.TryLoadDefault())
        : new EntryCreationTarget(Directory: Path.GetDirectoryName(place.LooseFull), Game: Provider.GameTarget, Crypto: Provider.Crypto);

    /// <summary>What Copy path gives for a result, as it does for a file shown in its folder.</summary>
    private string CopyPathOf(SearchHit hit)
    {
        HitPlace place = PlaceOf(hit);
        return place.InArchive ? $"{place.RootFull}::{place.FromRoot}" : place.LooseFull;
    }

    /// <summary>
    /// The work of running <paramref name="work"/> on each group of <paramref name="hits"/> that one provider can see at
    /// once (the loose ones, then each RPF's), with a reader of its own that is closed after. Where the hits live and
    /// the game's keys are read here, on the UI thread, so the queued work owns everything it uses: ending the search
    /// meanwhile changes nothing for it.
    /// </summary>
    private Func<object?> ReaderWork<T>(IReadOnlyList<SearchHit> hits, Func<RpfProvider, IReadOnlyList<EntryRecord>, IEnumerable<T>> work)
    {
        HitPlace[] places = [.. hits.Select(PlaceOf)];
        LoadedGame? game = ReaderGame();
        return () => RunReader(places, game, work);
    }

    /// <summary>The game a reader of results loads, with the tab's own keys; none for an RPF searched on its own.</summary>
    private LoadedGame? ReaderGame() =>
        searchIndex is { StandaloneArchive: false } && Provider.GameTarget is { } target && Provider.Crypto is { } crypto
            ? new LoadedGame(Provider.GamePath, target, crypto)
            : null;

    private static List<T> RunReader<T>(HitPlace[] places, LoadedGame? game, Func<RpfProvider, IReadOnlyList<EntryRecord>, IEnumerable<T>> work)
    {
        var results = new List<T>();
        var reader = new RpfProvider();
        try
        {
            if (game is not null)
                reader.ApplyLoadedGame(game);
            foreach (var group in places.GroupBy(place => place.RootFull, StringComparer.OrdinalIgnoreCase))
            {
                var records = new List<EntryRecord>();
                if (group.Key.Length == 0)
                {
                    if (reader.InArchive)
                        reader.ShowGame();
                    foreach (HitPlace place in group)
                    {
                        string folder = Path.GetDirectoryName(place.Hit.Path.Replace('/', Path.DirectorySeparatorChar)) ?? "";
                        records.Add(reader.GameEntries(folder.Length == 0 ? "." : folder.Replace('\\', '/'))
                            .FirstOrDefault(entry => string.Equals(entry.Name, place.Hit.Name, StringComparison.OrdinalIgnoreCase))
                            ?? throw new FileNotFoundException($"{place.Hit.Name} is no longer there"));
                    }
                }
                else
                {
                    reader.OpenArchive(group.Key);
                    foreach (HitPlace place in group)
                        records.Add(reader.ArchiveEntryFromRoot(place.FromRoot)
                            ?? throw new FileNotFoundException($"{place.Hit.Name} is no longer in {Path.GetFileName(group.Key)}"));
                }
                results.AddRange(work(reader, records));
            }
        }
        finally
        {
            reader.Close();
        }
        return results;
    }

    // ---------------- The commands, for results ----------------

    /// <summary>
    /// Opens a result as Enter opens a file, without leaving the search: a texture dictionary or drawable in the texture
    /// editor (saving back into its RPF), a text file or an image in its viewer. An RPF is browsed, which a list of
    /// results can't do, so it opens in a tab of its own and this one keeps its search. Without <paramref name="open"/>,
    /// Open location: the result's folder, with it selected, which does end the search.
    /// </summary>
    private void OpenHit(SearchHit hit, bool open)
    {
        if (!open)
        {
            string path = hit.Path;
            EndSearch();
            GoToAddress(path);
            return;
        }
        string name = hit.Name;
        if (IsArchiveFile(name))
        {
            OpenInNewTabRequested?.Invoke(this, hit.Path);
            SetStatus($"Opened {name} in a new tab");
            return;
        }
        bool texture = name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase);
        bool text = TextDocument.IsTextName(name);
        bool image = Textures.TextureImport.IsImagePath(name);
        if (!texture && !text && !image)
        {
            SetStatus($"{name} has no viewer here; Open location shows it in its folder");
            return;
        }
        long limit = text ? TextDocument.MaxSize : image ? ImageViewerViewModel.MaxSize : long.MaxValue;
        if (hit.Size > limit)
        {
            SetStatus($"{name} is too large to view ({Formatting.FormatSize(hit.Size)})");
            return;
        }
        (byte[] Data, string Source, ArchiveEntryTarget? Target) file;
        try
        {
            // Read where it lives; the save target keeps only paths, so the reader can go at once.
            file = RunReader([PlaceOf(hit)], ReaderGame(), (reader, records) =>
                new[] { (reader.ReadEntryBytes(records[0]), reader.EntrySourcePath(records[0]),
                    reader.InArchive ? reader.GetArchiveEntryTarget(records[0]) : null) })[0];
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not open {name}: {error.Message}");
            return;
        }
        if (texture)
        {
            ArchiveEntryTarget? target = file.Target;
            TextureViewer.OpenDictionary(name, file.Source, file.Data,
                sourceSaver: target is not null ? target.Save : null,
                sourcePrepare: target is not null ? () => PrepareTextureWrite(target) : null,
                sourceRootPath: target?.RootPath ?? "");
            SetStatus($"Opening {name}");
        }
        else if (text)
        {
            if (!TextDocument.LooksLikeText(file.Data))
            {
                SetStatus($"{name} isn't a text file");
                return;
            }
            TextFileOpened?.Invoke(this, TextDocument.Load(name, file.Data));
            SetStatus($"Opened {name}");
        }
        else
        {
            ImageFileOpened?.Invoke(this, new ImageViewerViewModel(name, file.Data));
            SetStatus($"Opened {name}");
        }
    }

    /// <summary>Asks for a tab of its own at <c>path</c> (an RPF result, to browse it); the tabs open it.</summary>
    public event EventHandler<string>? OpenInNewTabRequested;

    /// <summary>Open location: the selected result's folder, with it selected.</summary>
    public void OpenSelectedLocation()
    {
        if (!searching || EntryOperationBusy)
            return;
        if (SelectedHits() is [var hit])
        {
            OpenHit(hit, open: false);
        }
        else if (SelectedEntries() is [var entry])
        {
            // A result of the folder's own search is already there: the folder, as it is, with it selected.
            EndSearch();
            SelectEntriesByName([entry.Name]);
        }
    }

    private void CopySearchPaths(IReadOnlyList<SearchHit> hits)
    {
        _ = AppServices.Clipboard.SetTextAsync(string.Join("\n", hits.Select(CopyPathOf)));
        SetStatus($"Copied {(hits.Count == 1 ? "path" : $"{hits.Count} paths")}");
    }

    private bool CopySearchFiles(IReadOnlyList<SearchHit> hits, string noun)
    {
        clipboardStaging?.Cleanup();
        var staging = new DragStaging();
        clipboardStaging = staging;
        return StartReadOperation($"Copying {noun}…", ReaderWork(hits, (reader, records) => reader.ExportEntries(records, staging.Name)),
            result => _ = PutOnClipboard((List<string>)result!, noun), "copy");
    }

    private bool ExtractSearchFiles(IReadOnlyList<SearchHit> hits, string folder, string noun) =>
        StartReadOperation($"Extracting {noun}…", ReaderWork(hits, (reader, records) => reader.ExtractEntries(records, folder)),
            _ =>
            {
                SetStatus($"Extracted {noun} to {folder}");
                ShowExtracted(folder);
            }, "extract");

    private bool ExtractSearchArchive(SearchHit hit, string folder) =>
        StartReadOperation($"Extracting {hit.Name}…", ReaderWork<string>([hit], (reader, records) => [reader.ExtractArchiveContents(records[0], folder)]),
            result =>
            {
                SetStatus($"Extracted {hit.Name} to {((List<string>)result!)[0]}");
                ShowExtracted(folder);
            }, "extract");

    private Task<EntryProperties?> ReadSearchProperties(SearchHit hit)
    {
        string before = Status;
        var result = new TaskCompletionSource<EntryProperties?>();
        bool started = StartReadOperation($"Reading {hit.Name}…", ReaderWork<EntryProperties>([hit], (reader, records) => [reader.DescribeEntry(records[0])]), described =>
        {
            SetStatus(before);
            // Results are found again as the index fills in: the same file is the same path, not the same object.
            bool still = searching && SelectedHits() is [var current] && current.Path == hit.Path;
            result.TrySetResult(still ? ((List<EntryProperties>)described!)[0] : null);
        }, "read the properties", () => result.TrySetResult(null));
        return started ? result.Task : Task.FromResult<EntryProperties?>(null);
    }

    /// <summary>The names already in a result's folder, as far as the index knows them.</summary>
    private IEnumerable<string> SearchSiblingNames(SearchHit hit)
    {
        string location = hit.Location;
        foreach (IndexedGroup group in searchIndex?.State.Groups ?? [])
        {
            int folder = Array.FindIndex(group.Folders, item => string.Equals(item, location, StringComparison.OrdinalIgnoreCase));
            if (folder < 0)
                continue;
            foreach (IndexedEntry entry in group.Files)
            {
                if (entry.Folder == folder && entry.Name != hit.Name)
                    yield return entry.Name;
            }
        }
    }

    private bool RenameSearchHit(SearchHit hit, string newName)
    {
        HitPlace place = PlaceOf(hit);
        EntryCreationTarget location = LocationOf(place);
        string current = hit.Name;
        return StartSearchWrite([location], $"rename {current}", $"Renaming {current}…",
            () => EntryOperations.RenameAt(location, current, newName),
            result => SetStatus($"Renamed {current} to {(string)result!}"));
    }

    private bool DeleteSearchHits(IReadOnlyList<SearchHit> hits)
    {
        var targets = new List<EntryDeletionTarget>();
        foreach (var group in hits.Select(PlaceOf).GroupBy(place => (place.RootFull, place.NestedPrefix)))
        {
            HitPlace first = group.First();
            EntryCreationTarget location = LocationOf(first) with { InternalDirectory = "." };
            targets.Add(first.InArchive
                ? new EntryDeletionTarget(location, [], [.. group.Select(place => place.LocalPath)])
                : new EntryDeletionTarget(location, [.. group.Select(place => place.LooseFull)], []));
        }
        string[] loose = [.. targets.Where(target => !target.InArchive).SelectMany(target => target.LoosePaths)];
        if (loose.Length > 0)
            LooseFilesAboutToBeDeleted?.Invoke(this, loose);
        int count = hits.Count;
        string noun = count == 1 ? "item" : "items";
        return StartSearchWrite([.. targets.Select(target => target.Location)], "delete selected items", $"Deleting {count} {noun}…",
            () => targets.Sum(target => target.InArchive ? EntryOperations.DeleteArchiveFilesAt(target) : EntryOperations.MoveFilesToTrash(target)),
            result => SetStatus($"Deleted {(int)result!} {((int)result! == 1 ? "item" : "items")}"));
    }

    /// <summary>
    /// A change to results wherever they live: every RPF written is prepared and reloaded as a folder's change is,
    /// the other tabs are told, and the index and the results catch up after.
    /// </summary>
    private bool StartSearchWrite(IReadOnlyList<EntryCreationTarget> locations, string failureLabel, string progress,
        Func<object?> work, Action<object?> done)
    {
        if (EntryOperationBusy)
            return false;
        entryOperationBusy = true;
        TextureViewer.SetExternalWriteBusy(true);
        RaiseEntryOperationStateChanged();
        SetStatus(progress);
        try
        {
            foreach (string archive in locations.Select(location => location.ArchivePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                PrepareArchiveWrite(archive);
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            SetStatus($"Could not {failureLabel}: {error.Message}");
            FinishEntryOperation();
            return false;
        }
        void Settle()
        {
            foreach (EntryCreationTarget location in locations)
            {
                RefreshOperationTarget(location);
                ContentChanged?.Invoke(this, location);
            }
            FinishEntryOperation();
            if (searching && searchIndex is not null)
                searchIndexTask = searchIndex.Refresh();
        }
        Enqueue(work, result =>
        {
            done(result);
            Settle();
        }, message =>
        {
            SetStatus($"Could not {failureLabel}: {message}");
            Settle();
        });
        return true;
    }
}
