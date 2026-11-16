using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HikariHopper.Core;
using HikariHopper.Rendering.World;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>A game's tab has two sides, Files and World: for RDR2, whose world HikariHopper shows.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class WorldFaceTests : ExplorerTestBase
{
    // A folder taken for an RDR2 installation, with a file and a folder of its own.
    private string MakeRdr2()
    {
        string root = MakeGame("Red Dead Redemption 2", "RDR2.exe");
        Directory.CreateDirectory(Path.Combine(root, "x64"));
        Write(Path.Combine(root, "notes.txt"), "rdr2");
        LoadHook = path => new LoadedGame(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), GameSettings.Rdr2Edition, Rdr2Keys.Bundled);
        return root;
    }

    private ExplorerViewModel OpenRdr2()
    {
        var tab = new ExplorerViewModel();
        tab.OpenGame(MakeRdr2());
        Settle([tab]);
        Assert.True(tab.HasGame);
        return tab;
    }

    [Fact]
    public void Only_a_game_whose_world_is_shown_has_a_World_side()
    {
        var gta = new ExplorerViewModel();
        gta.OpenGame(MakeGame());
        Settle([gta]);
        Assert.False(gta.HasWorld);
        gta.ShowWorld();
        Assert.False(gta.InWorld);

        ExplorerViewModel rdr2 = OpenRdr2();
        Assert.True(rdr2.HasWorld);
        Assert.Equal("Valentine", rdr2.WorldLandmark!.Name);
    }

    [Fact]
    public void The_World_side_starts_at_the_landmark_and_Back_and_Forward_go_through_both_sides()
    {
        ExplorerViewModel tab = OpenRdr2();
        tab.Navigate("x64");
        tab.ShowWorld();
        Assert.True(tab.InWorld);
        Assert.Equal("World", tab.TabTitle);
        Assert.False(tab.CanGoUp);
        WorldLandmark landmark = tab.WorldLandmark!;
        Assert.Equal(new WorldSpot(landmark.Eye, landmark.Yaw, landmark.Pitch).Coordinates, tab.WorldCoordinates);

        // The camera moves; Back is the files where they were left, Forward the world where the camera was.
        var moved = new WorldSpot(new System.Numerics.Vector3(-250, 800, 140), 1, .3f);
        tab.RememberWorldSpot(moved);
        Assert.Equal("-250.00, 800.00, 140.00", tab.WorldCoordinates);
        tab.GoBack();
        Assert.False(tab.InWorld);
        Assert.Equal("x64", tab.CurrentPath);
        Assert.Equal("x64", tab.TabTitle);
        int requests = 0;
        tab.WorldSpotRequested += (_, _) => requests++;
        tab.GoForward();
        Assert.True(tab.InWorld);
        Assert.Equal(moved, tab.WorldSpot);
        Assert.Equal(1, requests);

        // A folder asked for from the world is the files again, one more stop: Back is the world.
        tab.Navigate(".");
        Assert.False(tab.InWorld);
        Assert.Equal(".", tab.CurrentPath);
        tab.GoBack();
        Assert.True(tab.InWorld);
    }

    [Fact]
    public void The_world_opens_where_it_was_left_in_that_installation_until_the_game_closes()
    {
        ExplorerViewModel first = OpenRdr2();
        first.ShowWorld();
        var left = new WorldSpot(new System.Numerics.Vector3(-1200, 300, 60), -2, .1f);
        first.RememberWorldSpot(left);
        first.ShowFiles();

        // Another tab of the same installation starts there.
        var second = new ExplorerViewModel();
        second.OpenGame(first.GamePath);
        Settle([second]);
        Assert.Equal(left, second.WorldSpot);

        // Closing the game leaves the world for the files.
        second.ShowWorld();
        second.CloseWorkspace();
        Assert.False(second.InWorld);
        Assert.False(second.HasWorld);
    }

    // The explorer window with an RDR2 game open.
    private (MainWindow Window, ExplorerSession Session) OpenWindow()
    {
        string root = MakeRdr2();
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1360, Height = 840 };
        session.Attach(window);
        window.Show();
        session.Tabs.ActiveBridge.OpenGame(root);
        Settle(session.Tabs);
        TestWait.Pump(100);
        return (window, session);
    }

    private static T Find<T>(Window window) where T : Avalonia.Visual => window.GetVisualDescendants().OfType<T>().First();

    [AvaloniaFact]
    public void The_status_bar_switch_and_F6_flip_the_tab_between_its_files_and_its_world()
    {
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        StatusBar status = Find<StatusBar>(window);
        NavigationBar navigation = Find<NavigationBar>(window);
        WorkspaceView workspace = Find<WorkspaceView>(window);
        Assert.True(status.FaceSwitch.IsVisible);
        Assert.Equal(0, status.FaceSwitch.SelectedIndex);
        Assert.True(navigation.AddressBar.IsVisible);
        Assert.False(navigation.CoordinatesBar.IsVisible);

        // The globe: the world in the files' place, its address in the path's.
        status.FaceSwitch.Pick(1);
        TestWait.Pump(100);
        Assert.True(tab.InWorld);
        Assert.Equal(1, status.FaceSwitch.SelectedIndex);
        Assert.NotNull(workspace.World);
        Assert.True(workspace.WorldHost.IsVisible);
        Assert.False(workspace.Split.IsVisible);
        Assert.False(navigation.UpButton.IsVisible);
        Assert.False(navigation.AddressBar.IsVisible);
        Assert.False(navigation.SearchBox.IsVisible);
        Assert.False(navigation.ViewModeSlot.IsVisible);
        Assert.True(navigation.CoordinatesBar.IsVisible);
        // The time of day at the status bar's end, as the model viewer's footer has it.
        Assert.True(status.TimeWell.IsVisible);
        Assert.Equal(tab.WorldCoordinates, navigation.CoordinatesText.Text);
        Assert.True(status.WorldText.IsVisible);
        Assert.False(status.ItemCountText.IsVisible);

        // F6 back to the files, as they were.
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F6, RawInputModifiers.None);
        TestWait.Pump(100);
        Assert.False(tab.InWorld);
        Assert.True(workspace.Split.IsVisible);
        Assert.False(workspace.WorldHost.IsVisible);
        Assert.True(navigation.UpButton.IsVisible);
        Assert.True(navigation.AddressBar.IsVisible);
        Assert.True(navigation.SearchBox.IsVisible);
        Assert.False(navigation.CoordinatesBar.IsVisible);
        Assert.False(status.TimeWell.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_time_of_day_is_the_tabs_and_the_world_hides_under_a_menu()
    {
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        tab.ShowWorld();
        TestWait.Pump(100);
        StatusBar status = Find<StatusBar>(window);
        status.Dial.Hours = 21;
        tab.SetWorldHours(21);
        TestWait.Pump(50);
        Assert.Equal(TimeDial.Clock(21), status.TimeButton.Text);
        Assert.Equal(21, status.TimeGlyph.Hours);
        WorldView world = Find<WorkspaceView>(window).World!;
        Assert.Equal(21, world.Viewport.Hours);

        // A title-bar menu opened over it: the viewport, a native window that would cover it, hides.
        MenuRow menus = Find<MenuRow>(window);
        HikariHopper.Views.Controls.RetroMenu view = menus.MenuFor(menus.ViewButton);
        view.Open(menus.ViewButton);
        TestWait.Pump(50);
        Assert.True(world.Covered);
        Assert.False(world.Viewport.IsVisible);
        view.Close();
        TestWait.Pump(50);
        Assert.False(world.Covered);
        window.Close();
    }

    [AvaloniaFact]
    public void The_start_pages_globe_opens_a_game_with_a_world_on_its_World_side()
    {
        string rdr2 = MakeRdr2();
        string gta = MakeGame("Grand Theft Auto V", "GTA5.exe");
        AppSettings.SetString(GameSettings.Rdr2GameRootKey, rdr2);
        AppSettings.SetString(GameSettings.LegacyGameRootKey, gta);
        var real = LoadHook!;
        LoadHook = path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(gta).TrimEnd(Path.DirectorySeparatorChar)
            ? FakeLoadedGame(path) : real(path);
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1360, Height = 840 };
        session.Attach(window);
        window.Show();
        TestWait.Pump(100);
        LauncherRow[] rows = [.. window.GetVisualDescendants().OfType<LauncherRow>()];
        LauncherRow red = rows.Single(row => row.Entry.Game.Id == GameCatalog.Rdr2);
        Assert.All(rows.Where(row => row.Entry.Game.Id == GameCatalog.GtaV), row => Assert.Null(row.WorldButton));
        Assert.NotNull(red.WorldButton);

        red.WorldButton!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        TestWait.Until(() => tab.HasGame && !tab.GameLoading, "the game to load");
        TestWait.Pump(100);
        Assert.True(tab.InWorld);
        Assert.Equal("World", tab.TabTitle);
        // Back is its files, at its root.
        tab.GoBack();
        Assert.False(tab.InWorld);
        window.Close();
    }

    [AvaloniaFact]
    public void Until_its_index_is_ready_the_world_shows_its_loading_screen_and_opens_by_itself()
    {
        using var release = new ManualResetEventSlim();
        Rdr2World.Builder = (_, crypto, report, _) =>
        {
            report(new Rdr2WorldProgress("Reading the maps", .42));
            release.Wait(TimeSpan.FromSeconds(20));
            return EmptyWorld(crypto);
        };
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        // Started as the game opened.
        TestWait.Until(() => tab.WorldProgress is not null, "the world's index to report");
        Assert.True(tab.WorldPreparing);
        Assert.False(tab.WorldReady);
        tab.ShowWorld();
        TestWait.Pump(100);
        WorldView world = Find<WorkspaceView>(window).World!;
        Assert.True(world.Cover.IsVisible);
        Assert.False(world.Viewport.IsVisible);
        Assert.Equal("READING THE MAPS", world.Loading.Title);
        Assert.Equal("Preparing the world", world.Loading.Detail);
        Assert.Equal(.42, world.Loading.Progress);
        Assert.Equal("42%", world.Loading.ProgressText);
        Assert.Equal("Preparing the world", Find<StatusBar>(window).WorldText.Text);
        // The switch still works meanwhile.
        tab.ShowFiles();
        TestWait.Pump(50);
        Assert.False(world.Cover.IsVisible);
        tab.ShowWorld();

        release.Set();
        TestWait.Until(() => tab.WorldReady || tab.WorldFailure is not null, "the world's index");
        Assert.Null(tab.WorldFailure);
        TestWait.Pump(100);
        Assert.False(world.Cover.IsVisible);
        Assert.Equal("0 maps · 0 placements", tab.WorldStatus);
        window.Close();
    }

    [AvaloniaFact]
    public void Once_its_index_is_ready_the_world_streams_into_its_view_until_the_game_closes()
    {
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        TestWait.Until(() => tab.WorldReady || tab.WorldFailure is not null, "the world's index");
        tab.ShowWorld();
        TestWait.Pump(50);
        WorldView world = Find<WorkspaceView>(window).World!;
        Assert.True(world.Viewport.World);
        Assert.NotNull(world.Streamer);
        WorldStreamer streamer = world.Streamer!;
        // The files side pauses it, keeping what it streamed.
        tab.ShowFiles();
        TestWait.Pump(50);
        Assert.Same(streamer, world.Streamer);
        tab.CloseWorkspace();
        TestWait.Pump(50);
        Assert.Null(world.Streamer);
        window.Close();
    }

    [AvaloniaFact]
    public void A_picked_placements_menu_names_it_jumps_to_its_files_and_copies_it()
    {
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        TestWait.Until(() => tab.WorldReady || tab.WorldFailure is not null, "the world's index");
        string map = Write(Path.Combine(tab.Provider.GamePath, "x64", "val_02__strm_0.ymap"), "map");
        tab.ShowWorld();
        TestWait.Pump(50);
        WorldView world = Find<WorkspaceView>(window).World!;
        var placement = new WorldPlacement("val_bank", "val_02__strm_0.ymap · entity 50", null,
            new WorldFile("x64/val_02__strm_0.ymap", 3), new System.Numerics.Vector3(-306.42f, 771.15f, 120.61f));
        world.OpenMenu(placement, new Avalonia.Point(200, 150));
        TestWait.Pump(50);
        Assert.NotNull(world.Menu);
        List<TextBlock> texts = [.. world.Menu!.GetVisualDescendants().OfType<TextBlock>()];
        Assert.Equal("val_bank", texts[0].Text);
        Assert.Equal("val_02__strm_0.ymap · entity 50", texts[1].Text);
        List<PartsPanel.MenuRowItem> items = [.. world.Menu.GetVisualDescendants().OfType<PartsPanel.MenuRowItem>()];
        Assert.Equal(["Open model", "Show model in Files", "Show map file in Files", "Copy name", "Copy coordinates"], items.Select(item => item.Text));
        // Its model wasn't found: what needs it is off.
        Assert.False(items[0].IsEnabled);
        Assert.False(items[1].IsEnabled);
        Assert.True(items[2].IsEnabled);

        world.OpenMenu(placement, new Avalonia.Point(200, 150));
        TestWait.Pump(50);
        items = [.. world.Menu!.GetVisualDescendants().OfType<PartsPanel.MenuRowItem>()];
        items[4].Trigger();
        Assert.Equal("-306.42, 771.15, 120.61", Clipboard.Text);
        Assert.Null(world.Menu);

        // The map file shown in its folder, selected; Back returns to the world.
        world.OpenMenu(placement, new Avalonia.Point(200, 150));
        TestWait.Pump(50);
        items = [.. world.Menu!.GetVisualDescendants().OfType<PartsPanel.MenuRowItem>()];
        items[2].Trigger();
        Settle([tab]);
        Assert.False(tab.InWorld);
        Assert.Equal(["val_02__strm_0.ymap"], tab.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
        tab.GoBack();
        Assert.True(tab.InWorld);
        Assert.True(File.Exists(map));
        window.Close();
    }

    [AvaloniaFact]
    public void A_file_shown_inside_one_of_the_games_RPFs_keeps_its_world_and_Back_returns_to_it()
    {
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        TestWait.Until(() => tab.WorldReady || tab.WorldFailure is not null, "the world's index");
        SaveArchive(Path.Combine(tab.Provider.GamePath, "x64", "levels.rpf"), ("maps/val_02__strm_0.ymap", Bytes("map")));
        tab.ShowWorld();
        TestWait.Pump(50);
        WorldView world = Find<WorkspaceView>(window).World!;
        WorldStreamer streamer = world.Streamer!;
        tab.ShowWorldFile(new WorldFile("x64/levels.rpf/maps/val_02__strm_0.ymap", 3));
        Settle([tab]);
        TestWait.Pump(50);
        Assert.False(tab.InWorld);
        Assert.Equal(["val_02__strm_0.ymap"], tab.EntriesModel.Where(item => item.Selected).Select(item => item.Name));
        // The world is the game's, not the folder's: it stays, streamed as it was.
        Assert.True(tab.WorldReady);
        Assert.Same(streamer, world.Streamer);
        tab.GoBack();
        TestWait.Pump(50);
        Assert.True(tab.InWorld);
        Assert.True(tab.WorldReady);
        Assert.Same(streamer, world.Streamer);
        // And the files again where they were left.
        tab.ShowFiles();
        Settle([tab]);
        Assert.Contains("val_02__strm_0.ymap", ListedNames(tab));
        window.Close();
    }

    [AvaloniaFact]
    public void A_world_that_cant_be_prepared_says_why()
    {
        Rdr2World.Builder = (_, _, _, _) => throw new InvalidDataException("The game's content files couldn't be read");
        var (window, session) = OpenWindow();
        ExplorerViewModel tab = session.Tabs.ActiveBridge;
        TestWait.Until(() => tab.WorldFailure is not null, "the world's index to fail");
        tab.ShowWorld();
        TestWait.Pump(100);
        WorldView world = Find<WorkspaceView>(window).World!;
        Assert.True(world.Cover.IsVisible);
        Assert.Equal("COULDN'T PREPARE THE WORLD", world.Loading.Title);
        Assert.Equal("The game's content files couldn't be read", world.Loading.Detail);
        Assert.False(world.Loading.IsRunning);
        window.Close();
    }
}
