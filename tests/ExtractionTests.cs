using FiveFury.Rpf;
using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Extracting the selection, or an RPF's whole content, to a folder.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ExtractionTests : ExplorerTestBase
{
    private string Output(string name) => Path.GetFullPath(Path.Combine(Work, "..", "out-" + name));

    [AvaloniaFact]
    public async Task ExtractingFromAnArchiveWritesTheFilesAndFolders()
    {
        MakeGame();
        string path = SaveArchive(Path.Combine(Work, "mods.rpf"), ("data/carcols.meta", "<Item />"), ("readme.txt", "hello"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(path);
        bridge.SelectEntry(RowFor(bridge, "data"));
        bridge.SelectEntry(RowFor(bridge, "readme.txt"), Avalonia.Input.KeyModifiers.Control);
        string output = Output("archive");
        Dialogs.Folders.Enqueue(output);

        Assert.True(await bridge.ExtractSelection());
        Settle(bridge);
        Assert.Equal("<Item />", File.ReadAllText(Path.Combine(output, "data", "carcols.meta")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(output, "readme.txt")));
        Assert.Equal($"Extracted 2 items to {output}", bridge.Status);
        Assert.Equal("Extract to", Dialogs.Requests[^1].Title);

        // Again into the same folder: nothing is overwritten.
        Dialogs.Folders.Enqueue(output);
        Assert.True(await bridge.ExtractSelection());
        Settle(bridge);
        Assert.True(File.Exists(Path.Combine(output, "data (2)", "carcols.meta")));
        Assert.True(File.Exists(Path.Combine(output, "readme (2).txt")));
        // The next picker starts where the last extraction went.
        Assert.Equal(output, Dialogs.Requests[^1].StartPath);
    }

    [AvaloniaFact]
    public async Task ExtractingLooseFilesCopiesThem()
    {
        MakeGame();
        Write(Path.Combine(Work, "notes", "a.txt"), "a");
        Write(Path.Combine(Work, "loose.txt"), "loose");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(RowFor(bridge, "notes"));
        bridge.SelectEntry(RowFor(bridge, "loose.txt"), Avalonia.Input.KeyModifiers.Control);
        string output = Output("loose");
        Dialogs.Folders.Enqueue(output);

        Assert.True(await bridge.ExtractSelection());
        Settle(bridge);
        Assert.Equal("a", File.ReadAllText(Path.Combine(output, "notes", "a.txt")));
        Assert.Equal("loose", File.ReadAllText(Path.Combine(output, "loose.txt")));
        Assert.True(File.Exists(Path.Combine(Work, "loose.txt"))); // Copied, not moved.
    }

    [AvaloniaFact]
    public async Task AFolderWithADotIsNumberedAfterItsWholeName()
    {
        MakeGame();
        string path = SaveArchive(Path.Combine(Work, "mods.rpf"), ("data.v1/a.txt", "a"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(path);
        bridge.SelectEntry(RowFor(bridge, "data.v1"));
        string output = Output("dotted");
        // A file already has the folder's name there.
        Write(Path.Combine(output, "data.v1"), "file");
        Dialogs.Folders.Enqueue(output);

        Assert.True(await bridge.ExtractSelection());
        Settle(bridge);
        Assert.Equal("a", File.ReadAllText(Path.Combine(output, "data.v1 (2)", "a.txt")));
    }

    [AvaloniaFact]
    public async Task ExtractingIntoTheFolderShownListsWhatWasWritten()
    {
        MakeGame();
        SaveArchive(Path.Combine(Work, "mods.rpf"), ("readme.txt", "hello"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(RowFor(bridge, "mods.rpf"));
        Dialogs.Folders.Enqueue(Work);

        Assert.True(await bridge.ExtractArchiveContent());
        Settle(bridge);
        Assert.Contains("mods", bridge.EntriesModel.Select(item => item.Name));
    }

    [AvaloniaFact]
    public async Task ExtractingAllContentOfAnRpfMakesAFolderNamedAfterIt()
    {
        MakeGame();
        RpfArchive nested = Archive("inner.rpf", ("deep/x.txt", "x"));
        SaveArchive(Path.Combine(Work, "touhou.rpf"), ("textures/readme.txt", "r"), ("inner.rpf", nested));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(RowFor(bridge, "touhou.rpf"));
        Assert.True(bridge.CanExtractArchiveContent);
        string output = Output("content");
        Dialogs.Folders.Enqueue(output);

        Assert.True(await bridge.ExtractArchiveContent());
        Settle(bridge);
        string folder = Path.Combine(output, "touhou");
        Assert.Equal("r", File.ReadAllText(Path.Combine(folder, "textures", "readme.txt")));
        // A nested RPF stays an archive file.
        Assert.True(File.Exists(Path.Combine(folder, "inner.rpf")));
        Assert.Equal($"Extracted touhou.rpf to {folder}", bridge.Status);

        // A nested RPF's own content, from inside its parent.
        bridge.OpenArchive(Path.Combine(Work, "touhou.rpf"));
        bridge.SelectEntry(RowFor(bridge, "inner.rpf"));
        Dialogs.Folders.Enqueue(output);
        Assert.True(await bridge.ExtractArchiveContent());
        Settle(bridge);
        Assert.Equal("x", File.ReadAllText(Path.Combine(output, "inner", "deep", "x.txt")));

        // Only an RPF has content to extract.
        bridge.SelectEntry(RowFor(bridge, "textures"));
        Assert.False(bridge.CanExtractArchiveContent);
        Assert.False(await bridge.ExtractArchiveContent());
    }

    [AvaloniaFact]
    public async Task CancellingThePickerExtractsNothing()
    {
        MakeGame();
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.SelectEntry(0);
        Assert.False(await bridge.ExtractSelection());
        Assert.False(bridge.EntryOperationBusy);
    }

    [AvaloniaFact]
    public void TheItemMenuOffersExtractingAndAnArchiveItsContent()
    {
        MakeGame();
        SaveArchive(Path.Combine(Work, "touhou.rpf"), ("readme.txt", "r"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        var menu = new ExplorerContextMenu { DataContext = bridge };
        bridge.SelectEntry(RowFor(bridge, "touhou.rpf"));
        menu.Refresh();
        Assert.True(menu.ExtractItem.IsVisible);
        Assert.True(menu.ExtractContentItem.IsVisible);
        bridge.SelectEntry(0);
        menu.Refresh();
        Assert.True(menu.ExtractItem.IsVisible);
        Assert.False(menu.ExtractContentItem.IsVisible);
        bridge.ClearEntrySelection();
        menu.Refresh();
        Assert.False(menu.ExtractItem.IsVisible);
    }
}
