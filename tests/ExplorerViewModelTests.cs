using System.Collections.Specialized;
using Avalonia.Input;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>Port of <c>tests/test_bridge.py</c>.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ExplorerViewModelTests : ExplorerTestBase
{
    private static int PropertyChanges(ExplorerViewModel bridge, string property, Action action)
    {
        int count = 0;
        void Handler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == property)
                count++;
        }
        bridge.PropertyChanged += Handler;
        try { action(); }
        finally { bridge.PropertyChanged -= Handler; }
        return count;
    }

    private ExplorerViewModel OpenGameWith(params string[] files)
    {
        MakeGame();
        foreach (string name in files)
            Write(Path.Combine(Work, name), name);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        return bridge;
    }

    [AvaloniaFact]
    public void StartsWithoutDemoContent() => OnUiThread(() =>
    {
        var bridge = new ExplorerViewModel();

        Assert.False(bridge.HasWorkspace);
        Assert.False(bridge.HasGame);
        Assert.False(bridge.HasArchive);
        Assert.Equal("No game", bridge.ArchiveName);
        Assert.Equal(0, bridge.VisibleCount);
        Assert.Empty(bridge.TreeModel);
        Assert.Equal(-1, bridge.SelectedIndex);
        Assert.Equal(0, bridge.SelectionCount);
        Assert.Equal("list", bridge.ViewMode);
        Assert.Equal("New tab", bridge.TabTitle);
        Assert.Equal("No game loaded  ·  Configure game paths in Settings", bridge.Status);
        Assert.Equal("application/x-hikarihopper-entry-drag", bridge.EntryDragMimeType);
    });

    [AvaloniaFact]
    public void ViewModeAcceptsOnlyListAndGrid() => OnUiThread(() =>
    {
        var bridge = new ExplorerViewModel();
        int changes = PropertyChanges(bridge, nameof(ExplorerViewModel.ViewMode), () =>
        {
            bridge.SetViewMode("grid");
            bridge.SetViewMode("unsupported");
            bridge.SetViewMode("GRID");
        });
        Assert.Equal("grid", bridge.ViewMode);
        Assert.Equal(1, changes);

        changes += PropertyChanges(bridge, nameof(ExplorerViewModel.ViewMode), () => bridge.SetViewMode("list"));
        Assert.Equal("list", bridge.ViewMode);
        Assert.Equal(2, changes);
    });

    [AvaloniaFact]
    public void EntrySelectionSupportsControlShiftAndSelectAll() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt", "b.txt", "c.txt", "d.txt");

        bridge.SelectEntry(1);
        bridge.SelectEntry(3, KeyModifiers.Shift);
        Assert.Equal(3, bridge.SelectionCount);
        Assert.Equal(3, bridge.SelectedIndex);
        Assert.Equal("3 items", bridge.SelectedName);
        Assert.True(bridge.EntriesModel[2].Selected);

        bridge.SelectEntry(2, KeyModifiers.Control);
        Assert.Equal(2, bridge.SelectionCount);
        Assert.False(bridge.EntriesModel[2].Selected);

        bridge.SelectAllEntries();
        Assert.Equal(bridge.VisibleCount, bridge.SelectionCount);
        Assert.True(bridge.HasSelection);
        Assert.True(bridge.SelectionDeletable);
    });

    [AvaloniaFact]
    public void SingleSelectionReportsNameAndSize() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt");
        bridge.SelectEntry(RowFor(bridge, "a.txt"));
        Assert.Equal("a.txt", bridge.SelectedName);
        Assert.Equal("5 B", bridge.SelectedSize);
        bridge.ClearEntrySelection();
        Assert.Equal("", bridge.SelectedName);
        Assert.Equal("", bridge.SelectedSize);
        Assert.All(bridge.EntriesModel, item => Assert.False(item.Selected));
    });

    [AvaloniaFact]
    public void HeadersSortByNameTypeAndNumericSize() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "z_folder"));
        Directory.CreateDirectory(Path.Combine(Work, "a_folder"));
        File.WriteAllBytes(Path.Combine(Work, "small.ydr"), new byte[1]);
        File.WriteAllBytes(Path.Combine(Work, "large.ytd"), new byte[100]);
        File.WriteAllBytes(Path.Combine(Work, "medium.txt"), new byte[10]);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);

        bridge.SortEntries("name");
        Assert.False(bridge.SortAscending);
        Assert.Equal(["z_folder", "a_folder"], ListedNames(bridge).Take(2));

        List<long> FileSizes() => bridge.EntriesModel.Where(item => !item.IsDirectory).Select(item => item.Size).ToList();
        bridge.SortEntries("size");
        Assert.Equal("size", bridge.SortColumn);
        Assert.Equal(FileSizes().Order(), FileSizes());
        bridge.SortEntries("size");
        Assert.Equal(FileSizes().OrderDescending(), FileSizes());

        bridge.SortEntries("type");
        var kinds = bridge.EntriesModel.Where(item => !item.IsDirectory).Select(item => item.Kind.ToLowerInvariant()).ToList();
        Assert.Equal(kinds.Order(StringComparer.Ordinal), kinds);

        bridge.SortEntries("unknown");
        Assert.Equal("type", bridge.SortColumn);
    });

    [AvaloniaFact]
    public void SortingPreservesSelection() => OnUiThread(() =>
    {
        MakeGame();
        File.WriteAllBytes(Path.Combine(Work, "small.txt"), new byte[1]);
        File.WriteAllBytes(Path.Combine(Work, "large.txt"), new byte[100]);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        bridge.SelectEntry(RowFor(bridge, "small.txt"));

        bridge.SortEntries("size");

        Assert.Equal("small.txt", bridge.SelectedName);
        Assert.Equal(1, bridge.SelectionCount);
        Assert.Equal("small.txt", bridge.EntriesModel[bridge.SelectedIndex].Name);
        Assert.True(bridge.EntriesModel[bridge.SelectedIndex].Selected);
    });

    [AvaloniaFact]
    public void SearchFiltersCaseInsensitively() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("Alpha.txt", "beta.txt");
        bridge.SetSearch("ALP");
        Assert.Equal("ALP", bridge.SearchQuery);
        Assert.Equal(["Alpha.txt"], ListedNames(bridge));
        Assert.Equal(1, bridge.VisibleCount);
        Assert.Equal(3, bridge.TotalCount);
    });

    [AvaloniaFact]
    public void MarqueeSelectionReplacesAddsAndToggles() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt", "b.txt", "c.txt", "d.txt");
        bridge.BeginMarqueeSelection(KeyModifiers.None);
        bridge.UpdateMarqueeSelection(1, 3);
        bridge.UpdateMarqueeSelection(2, 3);
        bridge.EndMarqueeSelection();
        Assert.Equal(2, bridge.SelectionCount);

        bridge.BeginMarqueeSelection(KeyModifiers.Shift);
        bridge.UpdateMarqueeSelection(0, 1);
        bridge.EndMarqueeSelection();
        Assert.Equal(4, bridge.SelectionCount);

        bridge.BeginMarqueeSelection(KeyModifiers.Control);
        bridge.UpdateMarqueeSelection(1, 2);
        bridge.EndMarqueeSelection();
        Assert.Equal(2, bridge.SelectionCount);
        Assert.True(bridge.EntriesModel[0].Selected);
        Assert.True(bridge.EntriesModel[3].Selected);
    });

    [AvaloniaFact]
    public void GridMarqueeSelectsNonContiguousRows() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt", "b.txt", "c.txt", "d.txt");

        bridge.BeginMarqueeSelection(KeyModifiers.None);
        bridge.UpdateMarqueeRows([0, 2, 4, 99]);
        bridge.EndMarqueeSelection();

        Assert.Equal(3, bridge.SelectionCount);
        Assert.Equal([0, 2, 4], bridge.EntriesModel.Select((item, row) => (item, row))
            .Where(pair => pair.item.Selected).Select(pair => pair.row));
    });

    [AvaloniaFact]
    public void TreeSelectionUpdatesWithoutResettingTheModel() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "mods", "nested"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        // The tree starts inside the root: at the root nothing in it is selected.
        TreeItem mods = bridge.TreeModel[0];
        Assert.Equal("mods", mods.Label);
        Assert.False(mods.Selected);
        var actions = new List<NotifyCollectionChangedAction>();
        bridge.TreeModel.CollectionChanged += (_, e) => actions.Add(e.Action);

        bridge.Navigate("mods");

        Assert.Empty(actions);
        Assert.Same(mods, bridge.TreeModel[0]);
        Assert.True(mods.Selected);
    });

    [AvaloniaFact]
    public void TreeExpansionInsertsAndRemovesRowsWithoutReset() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "a", "child"));
        Directory.CreateDirectory(Path.Combine(Work, "b"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        TreeItem sibling = TreeRow(bridge, "b");
        var actions = new List<NotifyCollectionChangedAction>();
        bridge.TreeModel.CollectionChanged += (_, e) => actions.Add(e.Action);

        bridge.ToggleTreeNode("game://a");
        Assert.Equal([NotifyCollectionChangedAction.Add], actions);
        Assert.Equal(["a", "child", "b"], TreeLabels(bridge));
        Assert.Same(sibling, bridge.TreeModel[2]);

        bridge.ToggleTreeNode("game://a");
        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove], actions);
        Assert.Equal(2, bridge.TreeModel.Count);
    });

    [AvaloniaFact]
    public void TreeClickNavigatesWithoutExpandingFolder() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "mods", "nested"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);

        bridge.NavigateTree("game://mods");

        TreeItem mods = bridge.TreeModel[0];
        Assert.True(mods.Selected);
        Assert.False(mods.Expanded);
        Assert.Equal(1, bridge.TreeModel.Count);
    });

    [AvaloniaFact]
    public void TreeRowActivationNavigatesAndExpandsFolder() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "mods", "nested"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);

        bridge.ActivateTreeRow(0);

        Assert.Equal("mods", bridge.CurrentPath);
        Assert.True(bridge.TreeModel[0].Expanded);
        Assert.Equal("nested", bridge.TreeModel[1].Label);
    });

    [AvaloniaFact]
    public void OpensARealFiveFuryArchive() => OnUiThread(() =>
    {
        string path = SaveArchive(Path.Combine(Work, "sample.rpf"), ("folder/readme.txt", "hello"));
        var bridge = new ExplorerViewModel();

        bridge.OpenArchive(path);

        Assert.True(bridge.HasArchive);
        Assert.True(bridge.HasWorkspace);
        Assert.Equal("sample.rpf", bridge.ArchiveName);
        Assert.Equal("sample.rpf", bridge.TabTitle);
        Assert.Equal(1, bridge.VisibleCount);
        Assert.Equal(["folder"], TreeLabels(bridge));
        Assert.Equal(-1, bridge.SelectedIndex);
        Assert.Equal("Opened sample.rpf", bridge.Status);
    });

    [AvaloniaFact]
    public void NestedArchiveOpensFromFolderTree() => OnUiThread(() =>
    {
        string outer = SaveArchive(Path.Combine(Work, "outer.rpf"),
            ("nested.rpf", Archive("nested.rpf", ("inside/readme.txt", "nested"))));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(outer);

        Assert.Equal("nested.rpf", bridge.TreeModel[0].Label);
        string nestedPath = bridge.TreeModel[0].Path;
        bridge.NavigateTree(nestedPath);

        Assert.Equal("nested.rpf", bridge.ArchiveName);
        Assert.Equal("nested.rpf", bridge.TabTitle); // The tab names where it is.
        Assert.Equal(1, bridge.VisibleCount);
        Assert.Equal("inside", bridge.EntriesModel[0].Name);
        Assert.Equal(["nested.rpf", "inside"], TreeLabels(bridge));
        Assert.True(bridge.TreeModel[0].Expanded);
        Assert.True(bridge.TreeModel[0].Selected);
        Assert.Equal(nestedPath, bridge.TreeFocusPath);
        Assert.Equal(0, bridge.TreeFocusIndex);
        Assert.Equal("Opened nested archive", bridge.Status);
    });

    [AvaloniaFact]
    public void FolderActivationInsideNestedArchiveUsesLocalPath() => OnUiThread(() =>
    {
        var nested = Archive("netflix-force-4k-main.rpf", ("netflix-force-4k-main", null!));
        string outer = SaveArchive(Path.Combine(Work, "outer.rpf"), ("New folder/netflix-force-4k-main.rpf", nested));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(outer);

        bridge.ActivateEntry(0);
        bridge.ActivateEntry(0);
        bridge.ActivateEntry(0);

        Assert.Equal("netflix-force-4k-main", bridge.CurrentPath);
        Assert.Equal(["new folder", "netflix-force-4k-main.rpf", "netflix-force-4k-main"],
            bridge.NavigationPathSegments.Select(segment => segment.Label));
        Assert.Equal(["new folder", "netflix-force-4k-main.rpf", "netflix-force-4k-main"], TreeLabels(bridge));
        Assert.Equal("netflix-force-4k-main", bridge.TabTitle);
    });

    [AvaloniaFact]
    public void GameFolderIsThePrimaryWorkspace() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "mods", "nested"));
        SaveArchive(Path.Combine(Work, "x64a.rpf"), ("common/data/example.xml", "<example />"));
        string name = Path.GetFileName(Work);
        var bridge = new ExplorerViewModel();
        var opened = new List<string>();
        bridge.GameOpened += (_, path) => opened.Add(path);
        bridge.OpenGame(Work);

        Assert.Equal([Work], opened);
        Assert.True(bridge.HasWorkspace);
        Assert.True(bridge.HasGame);
        Assert.False(bridge.HasArchive);
        Assert.Equal(Work, bridge.GamePath);
        Assert.Equal(3, bridge.VisibleCount);
        Assert.Equal(["mods", "x64a.rpf"], TreeLabels(bridge));
        Assert.Equal(-1, bridge.SelectedIndex);
        Assert.Equal("Loaded GTA V Legacy", bridge.Status);
        Assert.Equal(name, bridge.TabTitle);

        bridge.SelectEntry(0);
        Assert.Equal(0, bridge.SelectedIndex);
        bridge.Navigate("mods");
        Assert.Equal("mods", bridge.TabTitle);
        Assert.Equal(-1, bridge.SelectedIndex);
        Assert.True(bridge.CanGoBack);
        bridge.GoBack();
        Assert.Equal(".", bridge.CurrentPath);
        Assert.True(bridge.CanGoForward);
        bridge.GoForward();
        Assert.Equal("mods", bridge.CurrentPath);
        bridge.GoUp();
        Assert.Equal(".", bridge.CurrentPath);
        Assert.False(bridge.CanGoUp);
        Assert.Equal(name, bridge.TabTitle);

        bridge.ActivateEntry(RowFor(bridge, "x64a.rpf"));

        Assert.True(bridge.HasArchive);
        Assert.True(bridge.InArchive);
        Assert.Equal("x64a.rpf", bridge.ArchiveName);
        Assert.Equal("x64a.rpf", bridge.TabTitle);
        Assert.Equal(["x64a.rpf"], bridge.ArchiveGamePathParts);
        Assert.Equal(["mods", "x64a.rpf", "common"], TreeLabels(bridge));

        bridge.ToggleTreeNode(bridge.TreeModel[2].Path);
        Assert.Equal("data", bridge.TreeModel[3].Label);
        Assert.False(bridge.TreeModel[3].HasChildren);

        bridge.ToggleTreeNode("rpf://x64a.rpf");
        Assert.Equal(["mods", "x64a.rpf"], TreeLabels(bridge));

        bridge.ToggleTreeNode("game://mods");
        Assert.Equal("nested", bridge.TreeModel[1].Label);
        Assert.False(bridge.TreeModel[1].HasChildren);
        bridge.ToggleTreeNode("game://mods");
        Assert.Equal(2, bridge.TreeModel.Count);
    });

    [AvaloniaFact]
    public void NestedArchiveKeepsCompleteGameTreeRoute() => OnUiThread(() =>
    {
        MakeGame();
        string name = Path.GetFileName(Work);
        var inner = Archive("inner.rpf", ("final/readme.txt", "inner"));
        SaveArchive(Path.Combine(Work, "outer.rpf"), ("branch/keep.txt", "outer"), ("branch/inner.rpf", inner));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        bridge.NavigateTree(TreeRow(bridge, "outer.rpf").Path);
        string branchPath = TreeRow(bridge, "branch").Path;
        bridge.ToggleTreeNode(branchPath);

        bridge.NavigateTree(TreeRow(bridge, "inner.rpf").Path);

        Assert.Equal(["outer.rpf", "branch", "inner.rpf", "final"], TreeLabels(bridge));
        Assert.True(bridge.TreeModel[0].Expanded);
        Assert.True(bridge.TreeModel[1].Expanded);
        Assert.True(bridge.TreeModel[2].Expanded);
        Assert.True(bridge.TreeModel[2].Selected);
        Assert.Equal(["outer.rpf", "branch", "inner.rpf"], bridge.ArchiveGamePathParts);
        Assert.Equal(["outer.rpf", "branch", "inner.rpf"], bridge.NavigationPathSegments.Select(segment => segment.Label));
        Assert.Equal([true, false, true], bridge.NavigationPathSegments.Select(segment => segment.Archive));
        Assert.Equal("rpf://outer.rpf", bridge.NavigationPathSegments[0].Target);

        bridge.NavigateTree(branchPath);

        Assert.Equal("outer.rpf", bridge.ArchiveName);
        Assert.Equal("branch", bridge.CurrentPath);
        Assert.Equal(branchPath, bridge.TreeFocusPath);
        Assert.Equal(["outer.rpf", "branch", "inner.rpf", "final"], TreeLabels(bridge));
    });

    [AvaloniaFact]
    public void TreeFocusFollowsPathWhenRowsAreInserted() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "a_folder", "child"));
        SaveArchive(Path.Combine(Work, "z_archive.rpf"), ("readme.txt", "archive"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        string archivePath = TreeRow(bridge, "z_archive.rpf").Path;
        bridge.FocusTreePath(archivePath);
        int previousIndex = bridge.TreeFocusIndex;

        bridge.Navigate("a_folder/child");

        Assert.Equal(archivePath, bridge.TreeFocusPath);
        Assert.Equal(previousIndex + 1, bridge.TreeFocusIndex);
    });

    [AvaloniaFact]
    public void DeeplyNestedArchiveKeepsEveryArchiveNode() => OnUiThread(() =>
    {
        MakeGame();
        string name = Path.GetFileName(Work);
        var deepest = Archive("deepest.rpf", ("final/readme.txt", "deepest"));
        var middle = Archive("middle.rpf", ("next/deepest.rpf", deepest));
        SaveArchive(Path.Combine(Work, "outer.rpf"), ("branch/middle.rpf", middle));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        bridge.NavigateTree("rpf://outer.rpf");
        bridge.ToggleTreeNode(TreeRow(bridge, "branch").Path);
        bridge.NavigateTree(TreeRow(bridge, "middle.rpf").Path);
        bridge.ToggleTreeNode(TreeRow(bridge, "next").Path);
        string deepestPath = TreeRow(bridge, "deepest.rpf").Path;

        bridge.NavigateTree(deepestPath);

        Assert.Equal(["outer.rpf", "branch", "middle.rpf", "next", "deepest.rpf", "final"], TreeLabels(bridge));
        Assert.Equal(deepestPath, bridge.TreeFocusPath);
        Assert.Equal(["outer.rpf", "branch", "middle.rpf", "next", "deepest.rpf"], bridge.ArchiveGamePathParts);
    });

    [AvaloniaFact]
    public void LoadedFlatRpfHasNoTreeExpansionControl() => OnUiThread(() =>
    {
        var flat = Archive("flat.rpf", ("navmesh.ynv", "flat"));
        var structured = Archive("structured.rpf", ("folder/readme.txt", "structured"));
        string outer = SaveArchive(Path.Combine(Work, "outer.rpf"), ("flat.rpf", flat), ("structured.rpf", structured));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(outer);

        bridge.NavigateTree(TreeRow(bridge, "flat.rpf").Path);
        TreeItem flatRow = TreeRow(bridge, "flat.rpf");
        Assert.True(flatRow.Selected);
        Assert.False(flatRow.HasChildren);
        Assert.False(flatRow.Expanded);

        bridge.NavigateTree(TreeRow(bridge, "structured.rpf").Path);
        TreeItem structuredRow = TreeRow(bridge, "structured.rpf");
        Assert.True(structuredRow.HasChildren);
        Assert.True(structuredRow.Expanded);
    });

    [AvaloniaFact]
    public void UnopenedFlatGameRpfsLoseTheirExpandArrow() => OnUiThread(() =>
    {
        MakeGame();
        SaveArchive(Path.Combine(Work, "flat.rpf"), ("navmesh.ynv", "flat"));
        SaveArchive(Path.Combine(Work, "structured.rpf"), ("folder/readme.txt", "structured"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);

        // Until its table has been read in the background, an unopened RPF may have children.
        Assert.True(TreeRow(bridge, "flat.rpf").HasChildren);
        WaitFor(() => !TreeRow(bridge, "flat.rpf").HasChildren);
        Assert.True(TreeRow(bridge, "structured.rpf").HasChildren);
    });

    [AvaloniaFact]
    public void UnopenedFlatNestedRpfsLoseTheirExpandArrow() => OnUiThread(() =>
    {
        var flat = Archive("flat.rpf", ("navmesh.ynv", "flat"));
        var structured = Archive("structured.rpf", ("inner.rpf", Archive("inner.rpf", ("a.txt", "a"))));
        string outer = SaveArchive(Path.Combine(Work, "outer.rpf"), ("flat.rpf", flat), ("structured.rpf", structured));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(outer);

        WaitFor(() => !TreeRow(bridge, "flat.rpf").HasChildren);
        // A nested RPF counts as something to expand, like a folder.
        Assert.True(TreeRow(bridge, "structured.rpf").HasChildren);
        Assert.False(TreeRow(bridge, "flat.rpf").Expanded);
    });

    /// <summary>Runs the UI thread's queued work until <paramref name="condition"/> holds, for up to five seconds.</summary>
    private static void WaitFor(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Timed out waiting for the background archive read");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    [AvaloniaFact]
    public void ArchiveKeepsItsGameRelativeBreadcrumb() => OnUiThread(() =>
    {
        MakeGame();
        string archive = SaveArchive(Path.Combine(Work, "mods", "update", "update.rpf"), ("x64/data/example.xml", "<example />"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        bridge.OpenArchive(archive);

        Assert.Equal(["mods", "update", "update.rpf"], bridge.ArchiveGamePathParts);
        Assert.Empty(bridge.PathParts);

        bridge.Navigate("x64/data");

        Assert.Equal(["mods", "update", "update.rpf"], bridge.ArchiveGamePathParts);
        Assert.Equal(["x64", "data"], bridge.PathParts);
        Assert.Equal(["mods", "update", "update.rpf", "x64", "data"],
            bridge.NavigationPathSegments.Select(segment => segment.Label));
        Assert.Equal("game://mods", bridge.NavigationPathSegments[0].Target);
    });

    [AvaloniaFact]
    public void DirectoryNamedRpfNavigatesAsFolder() => OnUiThread(() =>
    {
        MakeGame();
        Directory.CreateDirectory(Path.Combine(Work, "mods", "update", "update.rpf", "x64"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        bridge.ToggleTreeNode("game://mods");
        bridge.ToggleTreeNode("game://mods/update");

        TreeItem row = TreeRow(bridge, "update.rpf");
        Assert.Equal("folder", row.Kind);
        Assert.Equal("game://mods/update/update.rpf", row.Path);

        bridge.NavigateTree(row.Path);

        Assert.False(bridge.InArchive);
        Assert.Equal("mods/update/update.rpf", bridge.CurrentPath);
        Assert.Equal(1, bridge.VisibleCount);
    });

    [AvaloniaFact]
    public void CopiesNamesAndPathsToTheClipboard() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt", "b.txt");
        bridge.SelectEntry(RowFor(bridge, "a.txt"));
        bridge.CopySelectedName();
        Assert.Equal("a.txt", Clipboard.Text);
        Assert.Equal("Copied name", bridge.Status);

        bridge.SelectAllEntries();
        bridge.CopySelectedPath();
        Assert.Equal(string.Join("\n", ListedNames(bridge).Select(name => Path.Combine(Work, name))), Clipboard.Text);
        Assert.Equal("Copied 3 paths", bridge.Status);

        string archive = SaveArchive(Path.Combine(Work, "inner.rpf"), ("data/file.txt", "x"));
        bridge.OpenArchive(archive);
        bridge.SelectEntry(0);
        bridge.CopySelectedPath();
        Assert.Equal($"{archive}::data", Clipboard.Text);
        Assert.Equal("Copied path", bridge.Status);
    });

    [AvaloniaFact]
    public void ArchivedDragStagesStandaloneCopiesUntilTheNextDrag() => OnUiThread(() =>
    {
        string archive = SaveArchive(Path.Combine(Work, "sample.rpf"),
            ("data/root.txt", "root"), ("data/nested/child.txt", "child"), ("loose.txt", "loose"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(archive);

        IReadOnlyList<string> first = bridge.StartEntryDrag(RowFor(bridge, "data"));
        string folder = Assert.Single(first);
        Assert.Equal("data", Path.GetFileName(folder));
        Assert.StartsWith("HikariHopper-", Path.GetFileName(Path.GetDirectoryName(folder)));
        Assert.Equal("root", File.ReadAllText(Path.Combine(folder, "root.txt")));
        Assert.Equal("child", File.ReadAllText(Path.Combine(folder, "nested", "child.txt")));
        Assert.Equal("Dragging one item as a copy", bridge.Status);

        bridge.SelectAllEntries();
        IReadOnlyList<string> second = bridge.StartEntryDrag(0);
        Assert.Equal(2, second.Count);
        Assert.False(Directory.Exists(Path.GetDirectoryName(folder)));
        Assert.Equal("Dragging 2 items as copies", bridge.Status);
    });

    [AvaloniaFact]
    public void LooseDragUsesTheGameFilesDirectly() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt");
        IReadOnlyList<string> paths = bridge.StartEntryDrag(RowFor(bridge, "a.txt"));
        Assert.Equal([Path.Combine(Work, "a.txt")], paths);
        Assert.Equal(1, bridge.SelectionCount);
    });

    [AvaloniaFact]
    public Task OpenArchiveDialogOpensThePickedArchive() => OnUiThreadAsync(async () =>
    {
        string archive = SaveArchive(Path.Combine(Work, "picked.rpf"), ("file.txt", "x"));
        var bridge = new ExplorerViewModel();
        Dialogs.OpenFiles.Enqueue([archive]);

        await bridge.OpenArchiveDialog();

        Assert.Equal("picked.rpf", bridge.ArchiveName);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Open RPF Archive", request.Title);
        Assert.Equal(["*.rpf"], request.Filters![0].Patterns);
    });

    [AvaloniaFact]
    public Task RpfSourcesSuggestArchiveNames() => OnUiThreadAsync(async () =>
    {
        var bridge = new ExplorerViewModel();
        Assert.Equal(new RpfSource("", ""), await bridge.ChooseRpfFolderSource());
        string folder = Directory.CreateDirectory(Path.Combine(Work, "stream")).FullName;
        Dialogs.Folders.Enqueue(folder);
        Assert.Equal(new RpfSource(folder, "stream.rpf"), await bridge.ChooseRpfFolderSource());
        string zip = Path.Combine(Work, "pack.zip");
        Dialogs.OpenFiles.Enqueue([zip]);
        Assert.Equal(new RpfSource(zip, "pack.rpf"), await bridge.ChooseRpfZipSource());
    });

    [AvaloniaFact]
    public void CloseWorkspaceReturnsToTheEmptyState() => OnUiThread(() =>
    {
        var bridge = OpenGameWith("a.txt");
        bridge.CloseWorkspace();
        Assert.False(bridge.HasWorkspace);
        Assert.Empty(bridge.EntriesModel);
        Assert.Empty(bridge.TreeModel);
        Assert.Equal(".", bridge.CurrentPath);
        Assert.Equal(-1, bridge.TreeFocusIndex);
        Assert.Equal("No game loaded  ·  Configure game paths in Settings", bridge.Status);
    });

    [AvaloniaFact]
    public void SearchFocusRequestIsForwarded() => OnUiThread(() =>
    {
        var bridge = new ExplorerViewModel();
        int requests = 0;
        bridge.SearchFocusRequested += (_, _) => requests++;
        bridge.RequestSearchFocus();
        Assert.Equal(1, requests);
    });

    [AvaloniaFact]
    public void TabsKeepIndependentExplorerState() => OnUiThread(() =>
    {
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        Directory.CreateDirectory(Path.Combine(enhanced, "mods"));
        string legacy = MakeGame("Legacy");
        Directory.CreateDirectory(Path.Combine(legacy, "update"));

        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = tabs.ActiveBridge;
        first.OpenGame(enhanced);
        first.Navigate("mods");
        first.SetViewMode("grid");

        tabs.NewTab();
        ExplorerViewModel second = tabs.ActiveBridge;
        second.OpenGame(legacy);
        second.Navigate("update");

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal(1, tabs.ActiveIndex);
        Assert.True(second.TabActive);
        Assert.False(first.TabActive);
        Assert.Equal(legacy, second.GamePath);
        Assert.Equal("update", second.CurrentPath);
        Assert.Equal("list", second.ViewMode);
        Assert.Same(second.EntriesModel, tabs.ActiveEntryModel);

        tabs.ActivateTab(0);

        Assert.Same(first, tabs.ActiveBridge);
        Assert.True(first.TabActive);
        Assert.Equal(enhanced, first.GamePath);
        Assert.Equal("mods", first.CurrentPath);
        Assert.Equal("grid", first.ViewMode);

        tabs.CloseActiveTab();

        Assert.Single(tabs.Tabs);
        Assert.Same(second, tabs.ActiveBridge);
        Assert.True(second.TabActive);
        Assert.Equal("update", second.CurrentPath);
    });

    [AvaloniaFact]
    public void ClosingTheLastTabClearsItsWorkspace() => OnUiThread(() =>
    {
        var tabs = new ExplorerTabsViewModel();
        MakeGame();
        tabs.ActiveBridge.OpenGame(Work);
        tabs.CloseTab(0);
        Assert.Single(tabs.Tabs);
        Assert.False(tabs.ActiveBridge.HasWorkspace);
    });

    [AvaloniaFact]
    public void TabCloseUsesIdentityAfterAnotherTabCloses() => OnUiThread(() =>
    {
        var tabs = new ExplorerTabsViewModel();
        tabs.NewTab();
        tabs.NewTab();
        ExplorerViewModel survivor = tabs.ActiveBridge;
        tabs.CloseTab(1);
        tabs.CloseTab(0);
        Assert.Equal([survivor], tabs.Tabs);
        Assert.Equal(0, tabs.ActiveIndex);
    });

    [AvaloniaFact]
    public void FileKindsUseFiveFuryLabels() => OnUiThread(() =>
    {
        Assert.Equal("Drawable", RpfProvider.FileKind("model.ydr"));
        Assert.Equal("Texture Dictionary", RpfProvider.FileKind("textures.ytd"));
        Assert.Equal("RPF Package", RpfProvider.FileKind("american_rel.rpf"));
    });
}
