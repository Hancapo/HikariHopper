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

    private static async Task Searched(ExplorerViewModel bridge)
    {
        for (int round = 0; round < 4; round++)
        {
            await bridge.SearchIndexSettled;
            Dispatcher.UIThread.RunJobs();
            await bridge.SearchSettled;
            Dispatcher.UIThread.RunJobs();
        }
        ExplorerStateRenders.Pump(100);
    }

    private static void Key(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        ExplorerStateRenders.Pump(100);
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
        ExplorerStateRenders.Pump(200);
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
        Assert.Equal("Game", navigation.ScopeText.Text);
        Assert.Same(navigation.SearchScopeSlot, navigation.ScopeWell.Parent);
        // Open with nothing typed, the folders stay as they were.
        Assert.True(workspace.FoldersFrame.IsVisible);

        navigation.QueryField.Text = "*.ydr";
        await Searched(bridge);
        // Results take the whole area, the folder panel included, and are always the list.
        Assert.False(workspace.FoldersFrame.IsVisible);
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
        Assert.True(workspace.FoldersFrame.IsVisible);
        Assert.Equal("Home", bridge.TabTitle);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheFolderSearchBoxHasTheSelectorAndItsOtherScopesSearch()
    {
        var (window, bridge, navigation, workspace) = Open();
        // Before the folder's box: Folder.
        Assert.True(navigation.ScopeWell.IsVisible);
        Assert.Same(navigation.ScopeSlot, navigation.ScopeWell.Parent);
        Assert.Equal("Folder", navigation.ScopeText.Text);

        // Text typed into the box comes along to whatever is chosen.
        navigation.SearchField.Text = "readme";
        ExplorerStateRenders.Pump(100);
        Point at = navigation.ScopeButton.TranslatePoint(new Point(20, 12), window)!.Value;
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        ExplorerStateRenders.Pump(200);
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
        Assert.Equal("Game", navigation.ScopeText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheFolderBoxOpensTheSearchRowOnTheFolderAndSubfoldersWidenIt()
    {
        var (window, bridge, navigation, workspace) = Open();
        navigation.SearchField.Focus();
        ExplorerStateRenders.Pump(200);
        // The search row, Folder chosen, the query taking the keyboard.
        Assert.True(bridge.IsSearching);
        Assert.Equal(SearchScope.Folder, bridge.SearchScopeValue);
        Assert.Equal("Folder", navigation.ScopeText.Text);
        Assert.True(navigation.QueryField.IsKeyboardFocusWithin);
        Assert.True(navigation.SubfoldersToggle.IsEnabled);
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

        // Only the folder has subfolders to add: with Game the toggle is off.
        bridge.BeginSearch(SearchScope.Everywhere);
        ExplorerStateRenders.Pump(100);
        Assert.False(navigation.SubfoldersToggle.IsEnabled);
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
        ExplorerStateRenders.Pump(300);
        table.ScrollViewer.Offset = new Vector(0, 6000);
        ExplorerStateRenders.Pump(200);
        Assert.Equal(6000, table.ScrollViewer.Offset.Y, 1);

        // The same search again, finding the same files: nothing moves.
        bridge.Refresh();
        await Searched(bridge);
        ExplorerStateRenders.Pump(200);
        Assert.Equal(6000, table.ScrollViewer.Offset.Y, 1);

        // Found again with a new file among them (the index caught up): the list changes in place, the selection
        // stays, and the view neither jumps to the top nor to the selected row.
        Write(Path.Combine(Work, "f00", "aaa_new.xml"), "n");
        bridge.Refresh();
        await Searched(bridge);
        ExplorerStateRenders.Pump(300);
        Assert.Equal(401, bridge.SearchHitCount);
        Assert.Equal(6000, table.ScrollViewer.Offset.Y, 1);
        Assert.Single(bridge.EntriesModel, item => item.Selected);

        // A new search starts at the top.
        navigation.QueryField.Text = "item0";
        await Searched(bridge);
        ExplorerStateRenders.Pump(300);
        Assert.Equal(0, table.ScrollViewer.Offset.Y, 1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AFolderChosenInTheTreeEndsTheSearchThere()
    {
        var (window, bridge, navigation, workspace) = Open();
        bridge.BeginSearch(SearchScope.Everywhere);
        navigation.QueryField.Text = "readme";
        await Searched(bridge);
        Assert.False(workspace.FoldersFrame.IsVisible);
        Assert.Equal(1, bridge.SearchHitCount);

        bridge.NavigateTree("game://mods");
        Settle(bridge);
        Assert.False(bridge.IsSearching);
        Assert.True(workspace.FoldersFrame.IsVisible);
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
        ExplorerStateRenders.Pump(300);
        viewer.Offset = new Vector(0, viewer.Extent.Height - viewer.Viewport.Height);
        ExplorerStateRenders.Pump(100);
        bridge.SelectEntry(bridge.EntriesModel.Count - 3);
        ExplorerStateRenders.Pump(50);

        // Wheeled up from above that row, past where the glide is: the view goes where the wheel takes it, to the top,
        // instead of being caught by the glide and pulled down with it.
        viewer.Offset = new Vector(0, 2000);
        ExplorerStateRenders.Pump(50);
        Point over = table.TranslatePoint(new Point(300, 200), window)!.Value;
        double last = viewer.Offset.Y;
        for (int notch = 0; notch < 50; notch++)
        {
            window.MouseWheel(over, new Vector(0, 1), RawInputModifiers.None);
            ExplorerStateRenders.Pump(30);
            Assert.True(viewer.Offset.Y <= last + 0.5, $"notch {notch}: {last} -> {viewer.Offset.Y}");
            last = viewer.Offset.Y;
        }
        ExplorerStateRenders.Pump(300);
        Assert.Equal(0, viewer.Offset.Y, 1);

        // Moving the current row again follows it as before.
        bridge.SelectEntry(bridge.EntriesModel.Count - 1);
        ExplorerStateRenders.Pump(300);
        Assert.True(viewer.Offset.Y > 0);
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
        Assert.True(workspace.FoldersFrame.IsVisible);
        Assert.Equal("mods", bridge.CurrentPath);
        Assert.Equal(["readme.txt"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
        window.Close();
    }
}
