using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FiveFury.Rpf;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Search mode in the window: the search row, results over the whole area, the tab's count.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class SearchViewTests : ExplorerTestBase
{
    public SearchViewTests() => ExplorerViewModel.SearchDelay = 0;

    private static void Key(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        TestWait.Settle();
    }

    private (MainWindow Window, ExplorerViewModel Bridge, NavigationBar Navigation, WorkspaceView Workspace) Open()
    {
        MakeGame();
        RpfArchive street = Archive("v_street.rpf", ("prop_bench_01a.ydr", "b"), ("prop_bin_01a.ydr", "b"));
        SaveArchive(Path.Combine(Work, "x64i.rpf"), ("levels/gta5/props/v_street.rpf", street));
        Write(Path.Combine(Work, "mods", "readme.txt"), "r");
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1200, Height = 640 };
        session.Attach(window);
        window.Show();
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        TestWait.Settle();
        return (window, bridge, window.GetVisualDescendants().OfType<NavigationBar>().Single(),
            window.GetVisualDescendants().OfType<WorkspaceView>().Single());
    }

    [AvaloniaFact]
    public async Task CtrlShiftFTurnsTheBarIntoTheSearchRowAndEscTurnsItBack()
    {
        var (window, bridge, navigation, workspace) = Open();
        Key(window, Avalonia.Input.Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        Assert.True(bridge.IsSearching);
        Assert.True(navigation.SearchRow.IsVisible);
        Assert.False(navigation.AddressBar.IsVisible);
        Assert.False(navigation.SearchField.IsVisible);
        Assert.True(navigation.QueryField.IsKeyboardFocusWithin);
        Assert.Equal("house", navigation.ScopeButton.IconKind);
        Assert.Same(navigation.SearchScopeSlot, navigation.ScopeWell.Parent);
        // Open with nothing typed, the folders stay as they were.
        Assert.True(workspace.ShowsFolders);

        navigation.QueryField.Text = "*.ydr";
        await Searched(bridge);
        // Results take the whole area, the folder panel included, and are always the list.
        Assert.False(workspace.ShowsFolders);
        Assert.IsType<EntryTable>(workspace.EntriesView);
        Assert.Equal("2 files", bridge.TabTitle);
        Assert.True(navigation.WildcardToggle.Raised);
        EntryTable table = Assert.IsType<EntryTable>(workspace.EntriesView);
        Assert.True(table.LocationWidth > 0);
        EntryRow row = window.GetVisualDescendants().OfType<EntryRow>().First();
        Assert.Contains(row.GetVisualChildren().OfType<Avalonia.Controls.TextBlock>(), cell => cell.IsVisible && cell.Inlines!.Count > 1
            && string.Concat(cell.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)) == "x64i.rpflevelsgta5propsv_street.rpf");

        Key(window, Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.False(bridge.IsSearching);
        Assert.True(navigation.AddressBar.IsVisible);
        Assert.True(navigation.SearchField.IsVisible);
        Assert.True(workspace.ShowsFolders);
        Assert.Equal("Home", bridge.TabTitle);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheFolderSearchBoxHasTheSelectorAndItsOtherScopesSearch()
    {
        var (window, bridge, navigation, workspace) = Open();
        // In the folder's box, at its end, as the icon of where it searches: Folder. No key cap; the text stops short.
        Assert.True(navigation.ScopeWell.IsVisible);
        Assert.Same(navigation.ScopeSlot, navigation.ScopeWell.Parent);
        Assert.Same(navigation.SearchBox, navigation.ScopeSlot.Parent);
        Assert.Equal("folder", navigation.ScopeButton.IconKind);
        Assert.Equal("", navigation.SearchField.KeyCap);
        Point scopeAt = navigation.ScopeButton.TranslatePoint(default, navigation.SearchField)!.Value;
        Assert.Equal(navigation.SearchField.Bounds.Width - 3, scopeAt.X + navigation.ScopeButton.Bounds.Width, 1);
        Assert.Equal((navigation.SearchField.Bounds.Height - navigation.ScopeButton.Bounds.Height) / 2, scopeAt.Y, 1);
        Assert.True(navigation.SearchField.Padding.Right >= navigation.ScopeButton.Bounds.Width + 3);

        // Text typed into the box comes along to whatever is chosen.
        navigation.SearchField.Text = "readme";
        TestWait.Settle();
        Point at = navigation.ScopeButton.TranslatePoint(new Point(20, 12), window)!.Value;
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        TestWait.Settle();
        var menu = Assert.IsType<Views.Controls.RetroMenu>(navigation.ScopeMenu);
        var items = menu.Items.OfType<Views.Controls.RetroMenuItem>().ToArray();
        // The names only, with icons; what each covers is a tooltip.
        Assert.Equal(["Folder", "RPF", "Game"], items.Select(item => item.Text));
        Assert.Equal(["folder", "package", "house"], items.Select(item => item.IconName));
        Assert.All(items, item => Assert.Equal("", item.ShortcutText));
        Assert.False(items[1].IsEnabled);

        // Game searches the whole game for what the box holds.
        items[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        await Searched(bridge);
        Assert.True(bridge.IsSearching);
        Assert.Equal("readme", bridge.SearchText);
        Assert.Equal("1 file", bridge.TabTitle);
        Assert.Equal("house", navigation.ScopeButton.IconKind);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheFolderBoxOpensTheSearchRowOnTheFolderAndSubfoldersWidenIt()
    {
        var (window, bridge, navigation, workspace) = Open();
        navigation.SearchField.Focus();
        TestWait.Settle();
        // The search row, Folder chosen, the query taking the keyboard.
        Assert.True(bridge.IsSearching);
        Assert.Equal(SearchScope.Folder, bridge.SearchScopeValue);
        Assert.Equal("folder", navigation.ScopeButton.IconKind);
        Assert.True(navigation.QueryField.IsKeyboardFocusWithin);
        Assert.True(navigation.SubfoldersToggle.IsVisible);
        Assert.False(navigation.SubfoldersToggle.Raised);

        window.KeyTextInput("mo");
        await Searched(bridge);
        Assert.Equal(["mods"], bridge.EntriesModel.Select(item => item.Name));
        Assert.False(((EntryTable)workspace.EntriesView!).LocationWidth > 0);

        // With subfolders, results come from under the folder, with where each lives.
        navigation.SubfoldersToggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        navigation.QueryField.Text = "readme";
        await Searched(bridge);
        Assert.True(navigation.SubfoldersToggle.Raised);
        Assert.Equal(["mods/readme.txt"], bridge.EntriesModel.Select(item => item.Path));
        Assert.True(((EntryTable)workspace.EntriesView!).LocationWidth > 0);

        // Only the folder has subfolders to add: with Game the toggle isn't there, and the query takes its room.
        double withToggle = navigation.QueryField.TrailingSpace;
        bridge.BeginSearch(SearchScope.Everywhere);
        TestWait.Settle();
        Assert.False(navigation.SubfoldersToggle.IsVisible);
        Assert.True(navigation.QueryField.TrailingSpace < withToggle);
        bridge.BeginSearch(SearchScope.Folder);
        TestWait.Settle();
        Assert.True(navigation.SubfoldersToggle.IsVisible);
        Assert.Equal(withToggle, navigation.QueryField.TrailingSpace);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ResultsFoundAgainKeepTheViewWhereItWasScrolled()
    {
        var (window, bridge, navigation, workspace) = Open();
        for (int folder = 0; folder < 10; folder++)
            for (int file = 0; file < 40; file++)
                Write(Path.Combine(Work, $"f{folder:00}", $"item{file:00}.xml"), "x");
        bridge.BeginSearch();
        navigation.QueryField.Text = ".xml";
        await Searched(bridge);
        EntryTable table = Assert.IsType<EntryTable>(workspace.EntriesView);
        // A row selected near the top, then the view scrolled far below it.
        bridge.SelectEntry(2);
        TestWait.Settle();
        table.ScrollViewer.Offset = new Vector(0, 6000);
        TestWait.Settle();
        Assert.Equal(6000, table.ScrollViewer.Offset.Y, 1);

        // The same search again, finding the same files: nothing moves.
        bridge.Refresh();
        await Searched(bridge);
        TestWait.Settle();
        Assert.Equal(6000, table.ScrollViewer.Offset.Y, 1);

        // Found again with a new file among them (the index caught up): the list changes in place, the selection
        // stays, and the view neither jumps to the top nor to the selected row.
        Write(Path.Combine(Work, "f00", "aaa_new.xml"), "n");
        bridge.Refresh();
        await Searched(bridge);
        TestWait.Settle();
        Assert.Equal(401, bridge.SearchHitCount);
        Assert.Equal(6000, table.ScrollViewer.Offset.Y, 1);
        Assert.Single(bridge.EntriesModel, item => item.Selected);

        // A new search starts at the top.
        navigation.QueryField.Text = "item0";
        await Searched(bridge);
        TestWait.Settle();
        Assert.Equal(0, table.ScrollViewer.Offset.Y, 1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheFolderPanelSlidesShutForResultsAndOpenAfter()
    {
        // Slowed down, so the slide caught midway is still midway on a busy machine.
        TimeSpan speed = WorkspaceView.FoldersSlideDuration;
        WorkspaceView.FoldersSlideDuration = TimeSpan.FromMilliseconds(1200);
        try
        {
            var (window, bridge, navigation, workspace) = Open();
            Avalonia.Controls.ColumnDefinition column = workspace.Split.ColumnDefinitions[0];
            double width = workspace.FoldersFrame.Bounds.Width;
            Assert.True(width > 100);

            // Typing slides it shut: midway it is narrower and fading, the panel inside at its full width, cut off.
            bridge.BeginSearch(SearchScope.Everywhere);
            navigation.QueryField.Text = "readme";
            TestWait.Pump(500);
            Assert.False(workspace.ShowsFolders);
            Assert.True(workspace.FoldersFrame.IsVisible);
            Assert.InRange(workspace.FoldersFrame.Bounds.Width, 1, width - 1);
            Assert.InRange(workspace.FoldersFrame.Opacity, 0.001, 0.999);
            Assert.Equal(width, workspace.Folders.Bounds.Width, 1);
            TestWait.Until(() => !workspace.FoldersFrame.IsVisible, "the folder panel to slide shut");
            Assert.Equal(0, column.Width.Value);

            // Leaving the search slides it open again, to the width it had.
            bridge.EndSearch();
            TestWait.Pump(500);
            Assert.True(workspace.ShowsFolders);
            Assert.InRange(workspace.FoldersFrame.Bounds.Width, 1, width - 1);
            TestWait.Until(() => workspace.FoldersFrame.Opacity == 1 && Math.Abs(workspace.FoldersFrame.Bounds.Width - width) < 1, "the folder panel to slide open");
            Assert.True(workspace.FoldersFrame.IsVisible);
            Assert.Equal(1, workspace.FoldersFrame.Opacity);
            Assert.Equal(width, workspace.FoldersFrame.Bounds.Width, 1);
            Assert.True(double.IsNaN(workspace.Folders.Width));
            window.Close();
        }
        finally
        {
            WorkspaceView.FoldersSlideDuration = speed;
        }
    }

    [AvaloniaFact]
    public async Task ResultsCantBeShownAsAGrid()
    {
        var (window, bridge, navigation, workspace) = Open();
        bridge.SetViewMode("grid");
        TestWait.Settle();
        Assert.IsType<EntryGrid>(workspace.EntriesView);

        // The search row open with nothing typed leaves the grid; the switch makes way for the row.
        double barRight = navigation.ViewModeSwitch.TranslatePoint(new Point(navigation.ViewModeSwitch.Bounds.Width, 0), navigation)!.Value.X;
        bridge.BeginSearch(SearchScope.Everywhere);
        // The switch's slot narrows to nothing as it fades.
        TestWait.Until(() => navigation.ViewModeSlot.Bounds.Width < 0.5, "the switch to make way for the search row");
        Assert.IsType<EntryGrid>(workspace.EntriesView);
        Assert.False(navigation.ShowsViewModeSwitch);
        Assert.Equal(0, navigation.ViewModeSlot.Bounds.Width, 1);
        Assert.Equal(barRight, navigation.QueryField.TranslatePoint(new Point(navigation.QueryField.Bounds.Width, 0), navigation)!.Value.X, 1);

        // Results: the list, with the grid not there to pick.
        navigation.QueryField.Text = "ydr";
        await Searched(bridge);
        Assert.IsType<EntryTable>(workspace.EntriesView);
        Assert.False(navigation.ShowsViewModeSwitch);
        Assert.False(bridge.CanChooseViewMode);
        bridge.SetViewMode("list");
        bridge.SetViewMode("grid");
        Assert.Equal("list", bridge.ShownViewMode);
        Assert.IsType<EntryTable>(workspace.EntriesView);

        // The search over, the grid comes back as it was.
        bridge.EndSearch();
        TestWait.Until(() => navigation.ViewModeSwitch.Opacity == 1
            && Math.Abs(navigation.ViewModeSwitch.TranslatePoint(new Point(navigation.ViewModeSwitch.Bounds.Width, 0), navigation)!.Value.X - barRight) < 0.5,
            "the switch to come back");
        Assert.IsType<EntryGrid>(workspace.EntriesView);
        Assert.Equal(1, navigation.ViewModeSwitch.SelectedIndex);
        Assert.True(navigation.ShowsViewModeSwitch);
        Assert.Equal(barRight, navigation.ViewModeSwitch.TranslatePoint(new Point(navigation.ViewModeSwitch.Bounds.Width, 0), navigation)!.Value.X, 1);
        Assert.Equal(1, navigation.ViewModeSwitch.Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EscEndsTheSearchFromTheResultsToo()
    {
        var (window, bridge, navigation, workspace) = Open();
        bridge.BeginSearch(SearchScope.Everywhere);
        navigation.QueryField.Text = "ydr";
        await Searched(bridge);
        EntryTable table = Assert.IsType<EntryTable>(workspace.EntriesView);
        bridge.SelectEntry(0);
        table.FocusTarget.Focus();
        TestWait.Pump(50);
        Assert.False(navigation.QueryField.IsKeyboardFocusWithin);

        // A menu open over the search closes first.
        Point at = navigation.ScopeButton.TranslatePoint(new Point(20, 12), window)!.Value;
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        TestWait.Settle();
        Assert.NotNull(navigation.ScopeMenu);
        Key(window, Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.Null(navigation.ScopeMenu);
        Assert.True(bridge.IsSearching);

        table.FocusTarget.Focus();
        Key(window, Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.False(bridge.IsSearching);
        Assert.True(workspace.ShowsFolders);
        Assert.True(navigation.AddressBar.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AFolderChosenInTheTreeEndsTheSearchThere()
    {
        var (window, bridge, navigation, workspace) = Open();
        bridge.BeginSearch(SearchScope.Everywhere);
        navigation.QueryField.Text = "readme";
        await Searched(bridge);
        Assert.False(workspace.ShowsFolders);
        Assert.Equal(1, bridge.SearchHitCount);

        bridge.NavigateTree("game://mods");
        Settle(bridge);
        Assert.False(bridge.IsSearching);
        Assert.True(workspace.ShowsFolders);
        Assert.Equal("mods", bridge.CurrentPath);
        Assert.Contains(bridge.EntriesModel, item => item.Name == "readme.txt");

        // The folder you were already in, chosen again: the search ends and its contents come back.
        bridge.BeginSearch();
        navigation.QueryField.Text = "nothing like it";
        await Searched(bridge);
        Assert.Empty(bridge.EntriesModel);
        bridge.NavigateTree("game://mods");
        Settle(bridge);
        Assert.False(bridge.IsSearching);
        Assert.Single(bridge.EntriesModel);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ScrollingPastAFarSelectionIsNotPulledBack()
    {
        var (window, bridge, navigation, workspace) = Open();
        for (int folder = 0; folder < 10; folder++)
            for (int file = 0; file < 40; file++)
                Write(Path.Combine(Work, $"f{folder:00}", $"item{file:00}.xml"), "x");
        bridge.BeginSearch();
        navigation.QueryField.Text = ".xml";
        await Searched(bridge);
        EntryTable table = Assert.IsType<EntryTable>(workspace.EntriesView);
        Avalonia.Controls.ScrollViewer viewer = table.ScrollViewer;
        // A row selected a screen down, then one near the end: the current row's highlight sets off on a glide of
        // thousands of pixels, which takes a while.
        bridge.SelectEntry(30);
        TestWait.Settle();
        viewer.Offset = new Vector(0, viewer.Extent.Height - viewer.Viewport.Height);
        TestWait.Settle();
        bridge.SelectEntry(bridge.EntriesModel.Count - 3);
        TestWait.Pump(50);

        // Wheeled up from above that row, past where the glide is: the view goes where the wheel takes it, to the top,
        // instead of being caught by the glide and pulled down with it.
        viewer.Offset = new Vector(0, 2000);
        TestWait.Pump(50);
        Point over = table.TranslatePoint(new Point(300, 200), window)!.Value;
        double last = viewer.Offset.Y;
        for (int notch = 0; notch < 50; notch++)
        {
            window.MouseWheel(over, new Vector(0, 1), RawInputModifiers.None);
            TestWait.Pump(30);
            Assert.True(viewer.Offset.Y <= last + 0.5, $"notch {notch}: {last} -> {viewer.Offset.Y}");
            last = viewer.Offset.Y;
            if (last < 0.5)
                break;
        }
        TestWait.Until(() => viewer.Offset.Y < 1, "the wheel to take the view to the top");
        TestWait.Settle();
        Assert.Equal(0, viewer.Offset.Y, 1);

        // Moving the current row again follows it as before.
        bridge.SelectEntry(bridge.EntriesModel.Count - 1);
        TestWait.Until(() => viewer.Offset.Y > 0, "the view to follow the current row");
        window.Close();
    }

    [AvaloniaFact]
    public async Task AResultsMenuIsTheItemMenuWithOpenLocation()
    {
        var (window, bridge, navigation, workspace) = Open();
        bridge.BeginSearch();
        navigation.QueryField.Text = "readme";
        await Searched(bridge);
        bridge.SelectEntry(0);
        ExplorerContextMenu menu = workspace.EntryContextMenu;
        menu.Refresh();
        Assert.True(menu.OpenLocationItem.IsVisible);
        Assert.Equal("Ctrl+Enter", menu.OpenLocationItem.ShortcutText);
        Assert.True(menu.RenameItem.IsVisible);
        Assert.False(menu.PasteItem.IsVisible);

        // Ctrl+Enter, with the results focused, goes to the file.
        (workspace.EntriesView as EntryTable)!.FocusTarget.Focus();
        Key(window, Avalonia.Input.Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter);
        Settle(bridge);
        Assert.False(bridge.IsSearching);
        Assert.True(workspace.ShowsFolders);
        Assert.Equal("mods", bridge.CurrentPath);
        Assert.Equal(["readme.txt"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
        window.Close();
    }
}
