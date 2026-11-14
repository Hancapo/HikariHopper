using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Show in Explorer and Open in Explorer: Windows Explorer at the item, the folder, or the RPF on disk.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ShowInExplorerTests : ExplorerTestBase, IDisposable
{
    private readonly List<(string Path, bool Select)> revealed = [];
    private readonly Action<string, bool> reveal = ExplorerViewModel.RevealInExplorer;

    public ShowInExplorerTests() => ExplorerViewModel.RevealInExplorer = (path, select) => revealed.Add((path, select));

    public new void Dispose()
    {
        ExplorerViewModel.RevealInExplorer = reveal;
        base.Dispose();
    }

    private ExplorerViewModel Game(out string archive)
    {
        MakeGame();
        Write(Path.Combine(Work, "mods", "readme.txt"), "r");
        archive = SaveArchive(Path.Combine(Work, "mods", "vehicles.rpf"), ("data/handling.meta", "<Item />"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        return bridge;
    }

    [AvaloniaFact]
    public void An_item_on_disk_is_shown_selected_and_the_folder_shown_is_opened()
    {
        ExplorerViewModel bridge = Game(out string archive);
        bridge.Navigate("mods");
        Settle(bridge);

        // The RPF selected: shown selected in its folder.
        bridge.SelectEntry(RowFor(bridge, "vehicles.rpf"));
        bridge.ShowInExplorer();
        // Nothing selected: the folder shown, opened.
        bridge.ClearEntrySelection();
        bridge.ShowInExplorer();
        Assert.Equal([(archive, true), (Path.Combine(Work, "mods"), false)], revealed);
    }

    [AvaloniaFact]
    public void Inside_an_rpf_its_file_on_disk_is_shown_selected()
    {
        ExplorerViewModel bridge = Game(out string archive);
        bridge.OpenArchive(archive);
        bridge.Navigate("data");
        Settle(bridge);
        bridge.SelectEntry(RowFor(bridge, "handling.meta"));
        bridge.ShowInExplorer();
        bridge.ClearEntrySelection();
        bridge.ShowInExplorer();
        Assert.Equal([(archive, true), (archive, true)], revealed);
    }

    [AvaloniaFact]
    public void Several_items_have_nowhere_to_go_and_the_menus_show_which_item_applies()
    {
        ExplorerViewModel bridge = Game(out _);
        bridge.Navigate("mods");
        Settle(bridge);
        var menu = new ExplorerContextMenu { DataContext = bridge };

        bridge.SelectEntry(RowFor(bridge, "vehicles.rpf"));
        menu.Refresh();
        Assert.True(menu.ShowInExplorerItem.IsVisible);
        Assert.True(menu.ShowInExplorerItem.IsEnabled);
        Assert.Equal("Ctrl+Shift+E", menu.ShowInExplorerItem.ShortcutText);
        Assert.False(menu.OpenInExplorerItem.IsVisible);

        bridge.SelectAllEntries();
        Assert.False(bridge.CanShowInExplorer);
        menu.Refresh();
        Assert.False(menu.ShowInExplorerItem.IsVisible);
        bridge.ShowInExplorer();
        Assert.Empty(revealed);

        bridge.ClearEntrySelection();
        menu.Refresh();
        Assert.True(menu.OpenInExplorerItem.IsVisible);
        Assert.True(menu.OpenInExplorerItem.IsEnabled);
        Assert.False(menu.ShowInExplorerItem.IsVisible);
    }

    [AvaloniaFact]
    public void Something_gone_from_disk_is_said_so_rather_than_opened()
    {
        ExplorerViewModel bridge = Game(out string archive);
        bridge.Navigate("mods");
        Settle(bridge);
        bridge.SelectEntry(RowFor(bridge, "vehicles.rpf"));
        File.Delete(archive);
        bridge.ShowInExplorer();
        Assert.Empty(revealed);
        Assert.Equal("Not found on disk: vehicles.rpf", bridge.Status);
    }
}
