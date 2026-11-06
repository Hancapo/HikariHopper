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
    public async Task AScopeKeepsTheSearchToAFolderOrAnRpf()
    {
        ExplorerViewModel bridge = Game();
        bridge.OpenArchive(inner);
        bridge.Navigate("levels/gta5/props");
        Settle(bridge);
        bridge.BeginSearch(SearchScope.FolderAndBelow);
        bridge.SetSearchText("readme");
        await Searched(bridge);
        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt"], Names(bridge));

        bridge.BeginSearch(SearchScope.Archive);
        await Searched(bridge);
        Assert.Equal(["x64i.rpf/levels/gta5/props/v_street.rpf/notes/readme.txt", "x64i.rpf/levels/gta5/readme.txt"], Names(bridge).Order());

        // "This folder" goes back to the folder, its filter holding the text.
        bridge.BeginSearch(SearchScope.Folder);
        Assert.False(bridge.IsSearching);
        Assert.Equal("readme", bridge.SearchQuery);
        Assert.Equal("levels/gta5/props", bridge.CurrentPath);
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

        bridge.BeginSearch();
        await Searched(bridge);
        Assert.Equal("readme", bridge.SearchText);
        bridge.GoBack();
        Assert.False(bridge.IsSearching);
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
