using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>File › Open folder… and folders dropped on the tab strip: a folder in a tab of its own, with a game.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class OpenFolderTests : ExplorerTestBase
{
    // A Legacy game with a file of its own, and a mod folder outside it: a loose file in a subfolder and an RPF.
    private (string Game, string Folder, ExplorerTabsViewModel Tabs) Setup()
    {
        string game = MakeGame("game");
        Write(Path.Combine(game, "common.txt"), "game");
        string folder = Directory.CreateDirectory(Path.Combine(Work, "mymod")).FullName;
        Write(Path.Combine(folder, "stream", "sign.txt"), "sign");
        SaveArchive(Path.Combine(folder, "dlc.rpf"), ("data/handling.meta", "<Item />"));
        var tabs = new ExplorerTabsViewModel();
        tabs.ActiveBridge.OpenGame(game);
        Settle(tabs);
        return (game, folder, tabs);
    }

    [AvaloniaFact]
    public void A_folder_opens_beside_its_games_tab_with_that_game_and_its_rpfs_open_with_the_games_keys()
    {
        var (game, folder, tabs) = Setup();
        ExplorerViewModel gameTab = tabs.ActiveBridge;
        Assert.Null(tabs.OpenFolders([folder], game, after: 0));
        Settle(tabs);

        Assert.Equal(2, tabs.Tabs.Count);
        ExplorerViewModel opened = tabs.Tabs[1];
        Assert.Same(opened, tabs.ActiveBridge);
        Assert.True(opened.HasFolder);
        Assert.Equal(folder, opened.FolderPath);
        // The game stays loaded behind it: its mark, its edition, its keys.
        Assert.True(opened.HasGame);
        Assert.Equal(game, opened.GamePath);
        Assert.Equal(GameSettings.LegacyEdition, opened.GameEditionId);
        Assert.Same(FakeCrypto, opened.Provider.Crypto);
        // Named for the folder, which is what it lists.
        Assert.Equal("mymod", opened.TabTitle);
        Assert.Equal("mymod", opened.RootTitle);
        Assert.Equal(["stream", "dlc.rpf"], ListedNames(opened));
        Assert.Equal(["stream", "dlc.rpf"], TreeLabels(opened));
        opened.Navigate("stream");
        Settle(opened);
        Assert.Equal(["sign.txt"], ListedNames(opened));
        Assert.Equal("stream", opened.TabTitle);

        // Its RPF opens inside its tree, with the game's keys.
        opened.Navigate(".");
        Settle(opened);
        opened.ActivateEntry(RowFor(opened, "dlc.rpf"));
        Settle(opened);
        Assert.True(opened.InArchive);
        Assert.False(opened.ArchiveIsRoot);
        Assert.Equal(["data"], ListedNames(opened));

        // The game's own tab is as it was.
        Assert.False(gameTab.HasFolder);
        Assert.Contains("common.txt", ListedNames(gameTab));
    }

    [AvaloniaFact]
    public void A_model_in_the_folder_opens_with_the_textures_beside_it_and_the_games_own()
    {
        var (game, folder, tabs) = Setup();
        Write(Path.Combine(folder, "stream", "sign.ydr"), "model");
        Write(Path.Combine(folder, "stream", "sign.ytd"), "textures");
        Write(Path.Combine(folder, "stream", "shared.ytd"), "more");
        tabs.OpenFolders([folder], game);
        Settle(tabs);
        ExplorerViewModel opened = tabs.ActiveBridge;
        opened.Navigate("stream");
        Settle(opened);
        ModelFile? model = null;
        opened.ModelFileOpened += (_, file) => model = file;
        opened.ActivateEntry(RowFor(opened, "sign.ydr"));
        Settle(opened);

        Assert.NotNull(model);
        // Its own dictionary and the others beside it, as anywhere.
        Assert.Equal(["textures"], model.Dictionaries.Select(System.Text.Encoding.UTF8.GetString));
        Assert.Equal(["shared.ytd"], model.Nearby.Select(dictionary => dictionary.Name));
        // The game's: the dictionaries its archetypes name, and its sky. Not where the game keeps a texture of a name,
        // which goes by a path in the game's folder, which the folder isn't in.
        Assert.NotNull(model.GameDictionaries);
        Assert.NotNull(model.GameSky);
        Assert.Null(model.GameHolding);
        // Saved where it is.
        Assert.NotNull(model.SaveTarget);
    }

    [AvaloniaFact]
    public void Close_goes_from_an_rpf_back_to_the_folder_then_from_the_folder_back_to_the_games_files()
    {
        var (game, folder, tabs) = Setup();
        tabs.OpenFolders([folder], game);
        Settle(tabs);
        ExplorerViewModel opened = tabs.ActiveBridge;
        opened.ActivateEntry(RowFor(opened, "dlc.rpf"));
        Settle(opened);
        Assert.True(opened.CanCloseArchiveOrFolder);

        opened.CloseArchiveOrFolder();
        Settle(opened);
        Assert.True(opened.HasFolder);
        Assert.Equal(["stream", "dlc.rpf"], ListedNames(opened));

        opened.CloseArchiveOrFolder();
        Settle(opened);
        Assert.False(opened.HasFolder);
        Assert.False(opened.CanCloseArchiveOrFolder);
        Assert.Contains("common.txt", ListedNames(opened));
        Assert.Equal(ExplorerViewModel.HomeTitle, opened.TabTitle);
    }

    [AvaloniaFact]
    public void What_is_written_or_deleted_stays_inside_the_folder()
    {
        var (game, folder, tabs) = Setup();
        tabs.OpenFolders([folder], game);
        Settle(tabs);
        RpfProvider provider = tabs.ActiveBridge.Provider;
        Assert.Equal(folder, provider.CreationTarget(".").Directory);
        Assert.Equal(Path.Combine(folder, "stream"), provider.CreationTarget("stream").Directory);
        Assert.Throws<ArgumentException>(() => provider.CreationTarget(".."));
        EntryRecord sign = provider.Entries("stream").Single();
        Assert.Equal([Path.Combine(folder, "stream", "sign.txt")], provider.DeletionTarget([sign], "stream").LoosePaths);
    }

    [AvaloniaFact]
    public void With_no_game_to_go_by_it_asks_which_unless_only_one_is_set_up()
    {
        string legacy = MakeGame("legacy");
        string enhanced = MakeGame("enhanced", "GTA5_Enhanced.exe");
        string folder = Directory.CreateDirectory(Path.Combine(Work, "mymod")).FullName;
        var tabs = new ExplorerTabsViewModel();

        // None set up: nothing opens, and the status bar says why.
        Assert.Null(tabs.OpenFolders([folder], null));
        Assert.Single(tabs.Tabs);
        Assert.Contains("Set up a game", tabs.ActiveBridge.Status);

        // One: it opens with it, without asking.
        AppSettings.SetString(GameSettings.LegacyGameRootKey, legacy);
        Assert.Null(tabs.OpenFolders([folder], null));
        Settle(tabs);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal(legacy, tabs.ActiveBridge.GamePath);
        Assert.Equal(folder, tabs.ActiveBridge.FolderPath);

        // Two: the games to choose from, in the start page's order, and nothing opened yet.
        AppSettings.SetString(GameSettings.EnhancedGameRootKey, enhanced);
        var games = tabs.OpenFolders([folder], null);
        Assert.NotNull(games);
        Assert.Equal([(GameSettings.EnhancedEdition, enhanced), (GameSettings.LegacyEdition, legacy)],
            games.Select(choice => (choice.Edition.Id, choice.Root)));
        Assert.Equal(2, tabs.Tabs.Count);
    }

    [AvaloniaFact]
    public void A_game_not_loaded_yet_loads_first_and_the_folder_opens_once_it_has()
    {
        string legacy = MakeGame("legacy");
        string folder = Directory.CreateDirectory(Path.Combine(Work, "mymod")).FullName;
        Write(Path.Combine(folder, "a.txt"), "a");
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel opened = tabs.OpenFolderInNewTab(folder, legacy);
        Settle(tabs);
        Assert.False(opened.GameLoading);
        Assert.Equal(legacy, opened.GamePath);
        Assert.Equal(folder, opened.FolderPath);
        Assert.Equal(["a.txt"], ListedNames(opened));
    }

    [AvaloniaFact]
    public async Task Search_in_a_folders_tab_finds_the_folders_files_where_they_are()
    {
        var (game, folder, tabs) = Setup();
        tabs.OpenFolders([folder], game);
        Settle(tabs);
        ExplorerViewModel opened = tabs.ActiveBridge;
        opened.BeginSearch();
        opened.SetSearchText("sign");
        await Searched(opened);
        Assert.Equal(["stream/sign.txt"], opened.EntriesModel.Select(item => item.Path));
        opened.SelectEntry(0, Avalonia.Input.KeyModifiers.None);
        opened.CopySelectedPath();
        Assert.Equal(Path.Combine(folder, "stream", "sign.txt"), Clipboard.Text);
    }

    [AvaloniaFact]
    public void A_folder_dropped_on_a_games_tab_opens_beside_it_and_on_the_strip_asks_for_the_game()
    {
        var (game, folder, tabs) = Setup();
        string enhanced = MakeGame("enhanced", "GTA5_Enhanced.exe");
        AppSettings.SetString(GameSettings.LegacyGameRootKey, game);
        AppSettings.SetString(GameSettings.EnhancedGameRootKey, enhanced);
        tabs.NewTab();
        var strip = new TabStrip { DataContext = tabs };
        var window = new Window { Width = 900, Height = 80, Content = strip };
        window.Show();
        TestWait.Pump(50);

        // Over the game's tab, it lights; over the empty strip, the strip does.
        TabStripTab gameTab = strip.TabAt(0)!;
        Point onGame = gameTab.TranslatePoint(new Point(40, 10), strip)!.Value;
        Assert.Same(gameTab, strip.GameTabAt(onGame));
        Assert.Null(strip.GameTabAt(strip.TabAt(1)!.TranslatePoint(new Point(40, 10), strip)!.Value));
        Assert.Null(strip.GameTabAt(new Point(880, 10)));
        strip.ShowDropTarget(gameTab, over: true);
        Assert.True(gameTab.DropTarget);
        Assert.False(strip.DropWashVisible);
        strip.ShowDropTarget(null, over: true);
        Assert.False(gameTab.DropTarget);
        Assert.True(strip.DropWashVisible);
        strip.ShowDropTarget(null, over: false);

        // A file alone isn't opened.
        Assert.False(strip.DropFolders(tabs, [Path.Combine(game, "common.txt")], tabs.Tabs[0]));
        Assert.Equal(2, tabs.Tabs.Count);

        // On the game's tab: just after it, with its game, without asking.
        Assert.True(strip.DropFolders(tabs, [folder], tabs.Tabs[0]));
        Settle(tabs);
        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Equal(folder, tabs.Tabs[1].FolderPath);
        Assert.Equal(game, tabs.Tabs[1].GamePath);
        Assert.Null(FolderOpening.Asking);

        // On the strip, or a tab with no game: asked, then at the end with the game chosen.
        Assert.True(strip.DropFolders(tabs, [folder], tabs.Tabs[2]));
        TestWait.Pump(50);
        GameChoiceDialog asking = FolderOpening.Asking!;
        Assert.Equal(2, asking.GameRows.Count);
        // Enhanced first, as the start page lists them: chosen to begin with.
        Assert.Equal(0, asking.ChosenIndex);
        asking.Choose(1);
        Assert.Equal(1, asking.ChosenIndex);
        asking.Choose(0);
        asking.Answer();
        Settle(tabs);
        Assert.Equal(4, tabs.Tabs.Count);
        Assert.Equal(enhanced, tabs.Tabs[3].GamePath);
        Assert.Equal(folder, tabs.Tabs[3].FolderPath);
        Assert.Equal(GameSettings.EnhancedEdition, tabs.Tabs[3].GameEditionId);
        window.Close();
    }
}
