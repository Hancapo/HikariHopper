using Avalonia.Threading;
using FiveFury.Rpf;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

/// <summary>Searching the whole game from a tab: results in the list, and the explorer's commands on them.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class SearchModeTests : ExplorerTestBase
{
    public SearchModeTests() => ExplorerViewModel.SearchDelay = 0;

    private static IEnumerable<string> Names(ExplorerViewModel bridge) => bridge.EntriesModel.Select(item => item.Path);

    private static int Row(ExplorerViewModel bridge, string path) =>
        Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Path == path);

    /// <summary>Waits for the index and the searches it sets off, and delivers their results.</summary>
    private static async Task Searched(ExplorerViewModel bridge)
    {
        for (int round = 0; round < 4; round++)
        {
            await bridge.SearchIndexSettled;
            Dispatcher.UIThread.RunJobs();
            await bridge.SearchSettled;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private string inner = "";
    private string update = "";

    private ExplorerViewModel Game()
    {
        MakeGame();
        Write(Path.Combine(Work, "mods", "readme.txt"), "loose readme");
        RpfArchive street = Archive("v_street.rpf", ("prop_bench_01a.ydr", "b"), ("prop_bin_01a.ydr", "b"), ("notes/readme.txt", "nested readme"));
        inner = SaveArchive(Path.Combine(Work, "x64i.rpf"), ("levels/gta5/props/v_street.rpf", street), ("levels/gta5/readme.txt", "archived readme"));
        update = SaveArchive(Path.Combine(Work, "update", "update.rpf"), ("common/data/carcols.ymt", "c"));
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        return bridge;
    }

    [AvaloniaFact]
    public async Task TheWholeGameIsSearchedAndTheTabCountsTheFiles()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("*.ydr");
        await Searched(bridge);
        Assert.True(bridge.IsSearching);
        Assert.True(bridge.SearchWildcards);
        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/prop_bench_01a.ydr", "x64i.rpf/levels/gta5/props/v_street.rpf/prop_bin_01a.ydr"],
            Names(bridge));
        Assert.Equal("2 files", bridge.TabTitle);

        bridge.SetSearchText("readme");
        await Searched(bridge);
        Assert.Equal(3, bridge.SearchHitCount);
        Assert.Equal("3 files", bridge.TabTitle);

        bridge.ToggleSearchRegex();
        bridge.SetSearchText("readme(");
        await Searched(bridge);
        Assert.StartsWith("Missing ) to close a group", bridge.SearchError);
        Assert.Equal("No files", bridge.TabTitle);
        Assert.Empty(bridge.EntriesModel);
    }

    [AvaloniaFact]
    public async Task AScopeKeepsTheSearchToAFolderItsSubfoldersOrAnRpf()
    {
        ExplorerViewModel bridge = Game();
        bridge.OpenArchive(inner);
        bridge.Navigate("levels/gta5/props");
        Settle(bridge);
        // The folder alone: its own listing, filtered, folders and RPFs included, with no index.
        bridge.BeginSearch(SearchScope.Folder);
        bridge.SetSearchText("v_");
        await Searched(bridge);
        Assert.True(bridge.IsSearching);
        Assert.False(bridge.SearchShowsLocation);
        Assert.Equal(["v_street.rpf"], bridge.EntriesModel.Select(item => item.Name));
        Assert.Equal("1 item", bridge.TabTitle);

        // With subfolders, everything under it, from the index.
        bridge.SetSearchText("readme");
        bridge.ToggleSearchSubfolders();
        await Searched(bridge);
        Assert.True(bridge.SearchShowsLocation);
        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"], Names(bridge));

        bridge.BeginSearch(SearchScope.Archive);
        await Searched(bridge);
        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt", "x64i.rpf/levels/gta5/readme.txt"], Names(bridge).Order());

        // Ending it shows the folder as it is.
        bridge.EndSearch();
        Assert.False(bridge.IsSearching);
        Assert.Equal("", bridge.SearchQuery);
        Assert.Equal("levels/gta5/props", bridge.CurrentPath);
        Assert.Equal(["v_street.rpf"], bridge.EntriesModel.Select(item => item.Name));
    }

    [AvaloniaFact]
    public async Task TheFolderIsSearchedWithTheSameMatching()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch(SearchScope.Folder);
        bridge.SetSearchText("*.rpf");
        await Searched(bridge);
        Assert.True(bridge.SearchWildcards);
        Assert.Equal(["x64i.rpf"], bridge.EntriesModel.Select(item => item.Name));
        bridge.ToggleSearchRegex();
        bridge.SetSearchText("^(mods|update)$");
        await Searched(bridge);
        Assert.Equal(["mods", "update"], bridge.EntriesModel.Select(item => item.Name));
        Assert.Equal("2 items", bridge.TabTitle);
        // A folder opened from the folder's search is where the search ends.
        bridge.ActivateEntry(0);
        Assert.False(bridge.IsSearching);
        Assert.Equal("mods", bridge.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenLocationGoesToTheFileAndBackEndsTheSearch()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("readme");
        await Searched(bridge);
        bridge.SelectEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"));
        bridge.OpenSelectedLocation();
        Settle(bridge);
        Assert.False(bridge.IsSearching);
        Assert.True(bridge.InArchive);
        Assert.Equal(@"x64i.rpf\levels\gta5\props\v_street.rpf\notes", bridge.AddressText);
        Assert.Equal(["readme.txt"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name));

        // The next search starts afresh.
        bridge.BeginSearch();
        await Searched(bridge);
        Assert.Equal("", bridge.SearchText);
        bridge.GoBack();
        Assert.False(bridge.IsSearching);
    }

    [AvaloniaFact]
    public async Task AResultAtANestedRpfsRootOpensThatRpf()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("prop_bench");
        await Searched(bridge);
        bridge.SelectEntry(0);
        bridge.OpenSelectedLocation();
        Settle(bridge);
        Assert.False(bridge.IsSearching);
        Assert.Equal(@"x64i.rpf\levels\gta5\props\v_street.rpf", bridge.AddressText);
        Assert.Equal(["prop_bench_01a.ydr"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
    }

    [AvaloniaFact]
    public async Task OpeningAResultOpensItWithoutLeavingTheSearch()
    {
        ExplorerViewModel bridge = Game();
        var ytd = new RpfArchive("textures.rpf");
        ytd.AddResource("vehicles/adder.ytd", Textures.TextureTestKit.Bytes(Textures.TextureTestKit.Dictionary("adder_body")));
        ytd.Save(Path.Combine(Work, "textures.rpf"));
        TextDocument? opened = null;
        bridge.TextFileOpened += (_, document) => opened = document;
        bridge.BeginSearch();
        bridge.SetSearchText("readme");
        await Searched(bridge);

        // A text file in a nested RPF: in the viewer, and the results stay.
        bridge.ActivateEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"));
        Assert.True(bridge.IsSearching);
        Assert.Equal("readme.txt", opened?.Name);
        Assert.Equal(3, bridge.SearchHitCount);

        // A texture dictionary in an RPF: in the texture editor, its saves going back to that RPF.
        bridge.SetSearchText("adder");
        await Searched(bridge);
        bridge.ActivateEntry(Row(bridge, "textures.rpf/vehicles/adder.ytd"));
        Assert.True(bridge.IsSearching);
        Assert.Equal("adder.ytd", bridge.TextureViewer.SourceName);
    }

    [AvaloniaFact]
    public async Task OpeningAnRpfResultBrowsesItInANewTab()
    {
        MakeGame();
        RpfArchive street = Archive("v_street.rpf", ("prop_bench_01a.ydr", "b"));
        SaveArchive(Path.Combine(Work, "x64i.rpf"), ("levels/gta5/props/v_street.rpf", street));
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel bridge = tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        bridge.BeginSearch();
        bridge.SetSearchText("v_street");
        await Searched(bridge);
        bridge.ActivateEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf"));
        Settle(tabs.ActiveBridge);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.True(tabs.Tabs[0].IsSearching);
        ExplorerViewModel opened = tabs.ActiveBridge;
        Assert.NotSame(bridge, opened);
        Assert.True(opened.InArchive);
        Assert.Equal(@"x64i.rpf\levels\gta5\props\v_street.rpf", opened.AddressText);
        Assert.Equal(["prop_bench_01a.ydr"], opened.EntriesModel.Select(item => item.Name));
    }

    [AvaloniaFact]
    public async Task CopyPathAndCopyWorkOnResultsWhereverTheyLive()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("readme");
        await Searched(bridge);
        bridge.SelectEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"));
        bridge.SelectEntry(Row(bridge, "mods/readme.txt"), Avalonia.Input.KeyModifiers.Control);
        bridge.CopySelectedPath();
        Assert.Equal($"{Path.Combine(Work, "mods", "readme.txt")}\n{inner}::levels/gta5/props/v_street.rpf/notes/readme.txt",
            string.Join("\n", Clipboard.Text!.Split('\n').Order()));

        Assert.True(await bridge.CopySelection());
        Settle(bridge);
        Assert.Equal(["loose readme", "nested readme"], Clipboard.Files.Select(File.ReadAllText).Order());
    }

    [AvaloniaFact]
    public async Task RenamingAndDeletingAResultSavesItsArchive()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("prop_b");
        await Searched(bridge);
        bridge.SelectEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf/prop_bench_01a.ydr"));
        Assert.True(bridge.CanRenameSelection);
        Assert.Equal("An entry with this name already exists", bridge.RenameNameError("prop_bin_01a.ydr"));
        Assert.True(bridge.RenameSelected("prop_bench_02.ydr"));
        Settle(bridge);
        await Searched(bridge);
        Assert.Equal("Renamed prop_bench_01a.ydr to prop_bench_02.ydr", bridge.Status);
        Assert.Contains("x64i.rpf/levels/gta5/props/v_street.rpf/prop_bench_02.ydr", Names(bridge));
        Assert.NotNull(ReadSavedEntry(inner, "levels/gta5/props/v_street.rpf/prop_bench_02.ydr"));

        // Delete, across a nested RPF and a loose file at once.
        bridge.SetSearchText("readme");
        await Searched(bridge);
        bridge.SelectEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"));
        bridge.SelectEntry(Row(bridge, "mods/readme.txt"), Avalonia.Input.KeyModifiers.Control);
        Assert.True(bridge.DeleteSelectedFiles());
        Settle(bridge);
        await Searched(bridge);
        Assert.Null(ReadSavedEntry(inner, "levels/gta5/props/v_street.rpf/notes/readme.txt"));
        Assert.Equal(["x64i.rpf/levels/gta5/readme.txt"], Names(bridge));
    }

    [AvaloniaFact]
    public async Task ACopyStartedJustBeforeTheSearchEndsStillCopies()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("readme");
        await Searched(bridge);
        bridge.SelectEntry(Row(bridge, "x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"));
        Assert.True(await bridge.CopySelection());
        // The search ends while the copy waits in the queue: it knows where its file lives all the same.
        bridge.EndSearch();
        Settle(bridge);
        Assert.Equal("nested readme", File.ReadAllText(Assert.Single(Clipboard.Files)));
    }

    [AvaloniaFact]
    public async Task PropertiesSurviveTheResultsBeingFoundAgain()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("carcols");
        await Searched(bridge);
        bridge.SelectEntry(0);
        Task<EntryProperties?> reading = bridge.ReadSelectionProperties();
        // The search runs again mid-read, as the index filling in makes it: new results, the same file selected.
        bridge.SetSearchText("carcols.");
        await Searched(bridge);
        Assert.Equal(["carcols.ymt"], bridge.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
        Settle(bridge);
        Assert.Equal("carcols.ymt", Assert.IsType<EntryProperties>(reading.Result).Name);
    }

    [AvaloniaFact]
    public async Task PropertiesDescribeAResultInItsArchive()
    {
        ExplorerViewModel bridge = Game();
        bridge.BeginSearch();
        bridge.SetSearchText("carcols");
        await Searched(bridge);
        bridge.SelectEntry(0);
        Task<EntryProperties?> reading = bridge.ReadSelectionProperties();
        Settle(bridge);
        EntryProperties properties = Assert.IsType<EntryProperties>(reading.Result);
        Assert.Equal("carcols.ymt", properties.Name);
        Assert.Equal("common/data/carcols.ymt", properties.Sections[0].Rows.Single(row => row.Label == "Path").Value);
        _ = update;
    }
}
