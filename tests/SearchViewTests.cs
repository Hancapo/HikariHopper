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
        Assert.Equal("Whole game", navigation.ScopeText.Text);
        // The results take the whole area: no folder panel, and always the list.
        Assert.False(workspace.FoldersFrame.IsVisible);
        Assert.IsType<EntryTable>(workspace.EntriesView);

        navigation.QueryField.Text = "*.ydr";
        await Searched(bridge);
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
        Assert.Equal("mods", bridge.CurrentPath);
        Assert.Equal(["readme.txt"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
        window.Close();
    }
}
