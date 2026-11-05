using FiveFury.Rpf;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Copying files and pasting them, in and out of RPFs, through the clipboard.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class CopyPasteTests : ExplorerTestBase
{
    private static int Row(ExplorerViewModel bridge, string name) =>
        Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == name);

    private static IEnumerable<string> Names(ExplorerViewModel bridge) => bridge.EntriesModel.Select(item => item.Name);

    private string Outside(string name)
    {
        string folder = Path.GetFullPath(Path.Combine(Work, "..", "outside-" + name));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [AvaloniaFact]
    public async Task CopyingLooseFilesPutsTheirPathsOnTheClipboard()
    {
        MakeGame();
        Write(Path.Combine(Work, "notes.txt"), "n");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(Row(bridge, "notes.txt"));
        Assert.True(await bridge.CopySelection());
        Assert.Equal([Path.Combine(Work, "notes.txt")], Clipboard.Files);
        Assert.Equal("Copied notes.txt", bridge.Status);
    }

    [AvaloniaFact]
    public async Task CopyingOutOfAnArchiveWritesTheFilesFirst()
    {
        MakeGame();
        string path = SaveArchive(Path.Combine(Work, "mods.rpf"), ("data/carcols.meta", "<Item />"), ("readme.txt", "hello"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(path);
        bridge.SelectEntry(Row(bridge, "data"));
        bridge.SelectEntry(Row(bridge, "readme.txt"), Avalonia.Input.KeyModifiers.Control);
        Assert.True(await bridge.CopySelection());
        Settle(bridge);
        // The copies are real files a paste anywhere can read.
        Assert.Equal(2, Clipboard.Files.Count);
        string data = Assert.Single(Clipboard.Files, file => Path.GetFileName(file) == "data");
        Assert.Equal("<Item />", File.ReadAllText(Path.Combine(data, "carcols.meta")));
        Assert.Equal("hello", File.ReadAllText(Assert.Single(Clipboard.Files, file => Path.GetFileName(file) == "readme.txt")));
        Assert.Equal("Copied 2 items", bridge.Status);
    }

    [AvaloniaFact]
    public async Task PastingIntoAFolderCopiesFilesAndFoldersUnderFreeNames()
    {
        MakeGame();
        string outside = Outside("loose");
        Write(Path.Combine(outside, "pack", "a.txt"), "a");
        Write(Path.Combine(outside, "file000.txt"), "outside");
        Write(Path.Combine(Work, "file000.txt"), "x");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        Clipboard.Files.AddRange([Path.Combine(outside, "pack"), Path.Combine(outside, "file000.txt")]);
        Assert.True(bridge.CanPaste);

        Assert.True(await bridge.Paste());
        Settle(bridge);
        Assert.Equal("a", File.ReadAllText(Path.Combine(Work, "pack", "a.txt")));
        // file000.txt was already here: the paste is file000 (2).txt, and the original is untouched.
        Assert.Equal("outside", File.ReadAllText(Path.Combine(Work, "file000 (2).txt")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(Work, "file000.txt")));
        Assert.Equal("Pasted 2 items", bridge.Status);
        Assert.Equal(["file000 (2).txt", "pack"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name).Order());

        // Pasted again, every name is taken: all get the next number.
        Assert.True(await bridge.Paste());
        Settle(bridge);
        Assert.Contains("pack (2)", Names(bridge));
        Assert.Contains("file000 (3).txt", Names(bridge));
    }

    [AvaloniaFact]
    public async Task PastingIntoAnArchiveAddsTheFilesAndFolders()
    {
        MakeGame();
        string outside = Outside("archive");
        Write(Path.Combine(outside, "pack", "deep", "b.txt"), "b");
        Write(Path.Combine(outside, "readme.txt"), "outside");
        string path = SaveArchive(Path.Combine(Work, "mods.rpf"), ("readme.txt", "inside"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(path);
        Clipboard.Files.AddRange([Path.Combine(outside, "pack"), Path.Combine(outside, "readme.txt")]);

        Assert.True(await bridge.Paste());
        Settle(bridge);
        Assert.Equal(["pack", "readme (2).txt", "readme.txt"], Names(bridge).Order());
        RpfArchive saved = ReadArchive(path);
        Assert.Equal("b", System.Text.Encoding.UTF8.GetString(((RpfFileEntry)saved.Find("pack/deep/b.txt")!).ReadLogical()));
        Assert.Equal("outside", System.Text.Encoding.UTF8.GetString(((RpfFileEntry)saved.Find("readme (2).txt")!).ReadLogical()));
    }

    [AvaloniaFact]
    public async Task ACopyOutOfOneArchivePastesIntoAnother()
    {
        MakeGame();
        string first = SaveArchive(Path.Combine(Work, "first.rpf"), ("textures/road.txt", "road"));
        string second = SaveArchive(Path.Combine(Work, "second.rpf"), ("keep.txt", "k"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(first);
        bridge.SelectEntry(Row(bridge, "textures"));
        Assert.True(await bridge.CopySelection());
        Settle(bridge);
        bridge.OpenArchive(second);
        Assert.True(await bridge.Paste());
        Settle(bridge);
        Assert.Equal("road", System.Text.Encoding.UTF8.GetString(((RpfFileEntry)ReadArchive(second).Find("textures/road.txt")!).ReadLogical()));
    }

    [AvaloniaFact]
    public async Task NothingToPasteSaysSoAndAFolderCannotGoIntoItself()
    {
        MakeGame();
        Write(Path.Combine(Work, "folder00", "inside.txt"), "i");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        Assert.False(bridge.CanPaste);
        Assert.False(await bridge.Paste());
        Assert.Equal("Nothing to paste", bridge.Status);

        bridge.Navigate("folder00");
        Clipboard.Files.Add(Path.Combine(Work, "folder00"));
        Assert.True(await bridge.Paste());
        Settle(bridge);
        Assert.StartsWith("Could not paste: A folder cannot be copied into itself", bridge.Status);
    }

    [AvaloniaFact]
    public void DroppingAFolderImportsItNow()
    {
        MakeGame();
        string outside = Outside("drop");
        Write(Path.Combine(outside, "dropped", "c.txt"), "c");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        Assert.True(bridge.ImportDroppedFiles([Path.Combine(outside, "dropped")]));
        Settle(bridge);
        Assert.Equal("c", File.ReadAllText(Path.Combine(Work, "dropped", "c.txt")));
    }

    [AvaloniaFact]
    public void TheMenusCopyItemsAndPasteIntoTheFolder()
    {
        MakeGame();
        Write(Path.Combine(Work, "file000.txt"), "x");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        var menu = new ExplorerContextMenu { DataContext = bridge };
        bridge.SelectEntry(0);
        menu.Refresh();
        Assert.True(menu.CopyItem.IsVisible);
        Assert.Equal("Ctrl+C", menu.CopyItem.ShortcutText);
        Assert.Equal("", menu.CopyNameItem.ShortcutText);
        Assert.False(menu.PasteItem.IsVisible);

        // On empty space, Paste is there, and on only while the clipboard holds files.
        bridge.ClearEntrySelection();
        menu.Refresh();
        Assert.True(menu.PasteItem.IsVisible);
        Assert.False(menu.PasteItem.IsEnabled);
        Clipboard.Files.Add(Path.Combine(Work, "file000.txt"));
        menu.Refresh();
        Assert.True(menu.PasteItem.IsEnabled);
    }
}
