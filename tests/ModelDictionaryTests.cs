using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

[Collection(ExplorerCollection.Name)]
public sealed class ModelDictionaryTests : ExplorerTestBase
{
    [AvaloniaFact]
    public void A_drawable_finds_its_texture_dictionary_even_when_the_folder_filter_hides_it()
    {
        MakeGame();
        File.WriteAllBytes(Path.Combine(Work, "prop_bench.ydr"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(Work, "prop_bench.ytd"), [4, 5, 6]);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        ModelFile? opened = null;
        bridge.ModelFileOpened += (_, file) => opened = file;

        // Only the drawable shows; its dictionary is still read with it.
        bridge.SetSearch(".ydr");
        Assert.DoesNotContain(bridge.EntriesModel, item => item.Name == "prop_bench.ytd");
        bridge.ActivateEntry(Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == "prop_bench.ydr"));
        Assert.NotNull(opened);
        Assert.Equal([(byte)4, 5, 6], Assert.Single(opened.Dictionaries));
    }

    [AvaloniaFact]
    public void The_dictionaries_near_a_drawable_are_searched_in_name_order_whatever_the_filter_and_sort()
    {
        MakeGame();
        File.WriteAllBytes(Path.Combine(Work, "prop_bench.ydr"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(Work, "b_shared.ytd"), [1]);
        File.WriteAllBytes(Path.Combine(Work, "a_shared.ytd"), [1, 2]);
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        ModelFile? opened = null;
        bridge.ModelFileOpened += (_, file) => opened = file;

        bridge.SortEntries("size");
        bridge.SortEntries("size");
        bridge.SetSearch(".ydr");
        bridge.ActivateEntry(Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == "prop_bench.ydr"));
        Assert.NotNull(opened);
        Assert.Equal(["a_shared.ytd", "b_shared.ytd"], opened.Nearby.Select(dictionary => dictionary.Name));
    }
}
