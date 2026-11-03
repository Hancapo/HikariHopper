using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Behaviour of the explorer views (ports of the QML explorer screens) on a fake game.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ExplorerViewTests : ExplorerTestBase
{
    // Title row 38 (with the menus) + tabs 36 + navigation 46 + gutter 8 + header 30 + list inset 4.
    private const double FirstRowTop = 162;
    // The entries panel: gutter 8 + folders 238 + gutter 8. Rows start 6px further in.
    private const double TableLeft = 254;
    // The grid has no list inset under its header.
    private const double GridTop = 158;
    // The folder tree's first row: below the gutter and the panel's 6px inset.
    private const double TreeTop = 134;

    private sealed record Explorer(MainWindow Window, ExplorerSession Session)
    {
        public ExplorerTabsViewModel Tabs => Session.Tabs;

        public ExplorerViewModel Bridge => Tabs.ActiveBridge;

        public T Find<T>() where T : Visual => Window.GetVisualDescendants().OfType<T>().First();
    }

    private Explorer OpenExplorer(int files = 6, int folders = 2, bool openGame = true, double width = 1360, double height = 840)
    {
        MakeGame();
        for (int index = 0; index < folders; index++)
            Directory.CreateDirectory(Path.Combine(Work, $"folder{index:00}"));
        for (int index = 0; index < files; index++)
            Write(Path.Combine(Work, $"file{index:000}.txt"), new string('x', index + 1));
        var session = new ExplorerSession();
        var window = new MainWindow { Width = width, Height = height };
        session.Attach(window);
        window.Show();
        if (openGame)
            session.Tabs.ActiveBridge.OpenGame(Work);
        Pump();
        return new Explorer(window, session);
    }

    private static void Pump(int milliseconds = 30) => ExplorerStateRenders.Pump(milliseconds);

    private static Point Row(int index, double x = 400) => new(x, FirstRowTop + index * 26 + 13);

    private static void Click(Window window, Point point, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseMove(point, modifiers);
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Pump();
    }

    private static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Pump();
    }

    [AvaloniaFact]
    public void StartPageShowsUntilAGameIsOpen()
    {
        Explorer ui = OpenExplorer(openGame: false);
        Assert.IsType<StartPage>(ui.Find<ExplorerScreen>().Page);
        Assert.Equal("HikariHopper — RPF Explorer", ui.Window.Title);

        ui.Bridge.OpenGame(Work);
        Pump();

        Assert.IsType<WorkspaceView>(ui.Find<ExplorerScreen>().Page);
        Assert.Equal($"HikariHopper — {ui.Bridge.TabTitle}", ui.Window.Title);
    }

    [AvaloniaFact]
    public void TheLauncherHasNoExplorerChromeAndListsTheGamesToOpen()
    {
        Explorer ui = OpenExplorer(openGame: false);
        ExplorerScreen screen = ui.Find<ExplorerScreen>();
        StartPage start = Assert.IsType<StartPage>(screen.Page);

        // None of the explorer: no tab strip for a lone launcher, no path bar, search, header or status.
        Assert.False(screen.StripVisible);
        Assert.False(ui.Find<TabStrip>().IsVisible);
        Assert.Empty(ui.Window.GetVisualDescendants().OfType<NavigationBar>());
        Assert.Empty(ui.Window.GetVisualDescendants().OfType<StatusBar>());
        Assert.DoesNotContain(start.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is "NAME" or "No installation open");
        Assert.Equal(StartPage.AppVersion, start.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Matches(@"^\d+\.\d+\.\d+", StartPage.AppVersion);

        // Nothing set up: GTA V gets one row that leads to Settings.
        LauncherRow lone = Assert.Single(start.Rows);
        Assert.Equal((HikariHopper.Core.GameCatalog.GtaV, false), (lone.Entry.Game.Id, lone.Entry.SetUp));
        Assert.Contains("Settings", string.Concat(lone.Detail.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)));
        bool settings = false;
        start.SettingsRequested += (_, _) => settings = true;
        start.Focus();
        lone.Focus(NavigationMethod.Tab);
        ui.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(settings);
        // It opens Settings as a modal over the launcher; dismiss it to carry on.
        SettingsDialog dialog = ui.Find<MenuRow>().Settings!;
        Assert.True(dialog.IsOpen);
        dialog.Close();
        Pump(250);

        // A configured edition is a row with its tinted V and its path, and a recent install of the
        // same game joins it under its folder name; the configured one isn't repeated.
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        string other = MakeGame("Modded GTA");
        HikariHopper.Core.AppSettings.SetString(HikariHopper.Core.GameSettings.EnhancedGameRootKey, enhanced);
        HikariHopper.Core.AppSettings.SetStringList(HikariHopper.Core.GameSettings.RecentGameRootsKey, [enhanced, other]);
        ui.Tabs.GamePathSettings.Refresh();
        Pump();
        LauncherRow[] rows = [.. start.Rows];
        Assert.Equal([("Enhanced", enhanced), ("Modded GTA", other)], rows.Select(row => (row.Entry.Title, row.Entry.Path)));
        Assert.Equal(HikariHopper.Ui.Theme.Selection, ((Views.Controls.EditionMark)rows[0].Mark).Fill);
        Assert.Equal(HikariHopper.Ui.Theme.TextDim, ((Views.Controls.EditionMark)rows[1].Mark).Fill);
        Assert.Equal(enhanced, rows[0].Detail.Text);

        // Clicking a row opens it, and the explorer and its tab strip come back.
        Click(ui.Window, rows[1].TranslatePoint(new Point(60, 20), ui.Window)!.Value);
        Settle(ui.Bridge);
        Pump();
        Assert.Equal(Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar), ui.Bridge.GamePath);
        Assert.IsType<WorkspaceView>(screen.Page);
        Assert.True(ui.Find<TabStrip>().IsVisible);

        // A second tab shows the launcher under the strip, which stays so the first tab is in reach.
        ui.Tabs.NewTab();
        Pump();
        Assert.IsType<StartPage>(screen.Page);
        Assert.True(ui.Find<TabStrip>().IsVisible);
        ui.Tabs.CloseTab(0);
        Pump();
        Assert.False(ui.Find<TabStrip>().IsVisible);
    }

    [AvaloniaFact]
    public void SettingsOpensAsAModalOverTheBlurredExplorer()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump(300);
        SettingsDialog settings = menus.Settings!;
        Assert.True(settings.IsOpen);
        Assert.True(ModalOverlay.IsOpen(ui.Window));
        // It's part of the main window, not a window of its own.
        Assert.Same(ui.Window, TopLevel.GetTopLevel(settings));

        // The explorer behind it blurs; the title bar stays out of the dim, but its menus are off.
        Control backdrop = ModalOverlay.BackdropOf(ui.Window)!;
        Assert.Same(ui.Window.ExplorerScreenHost, backdrop);
        Assert.Equal(ModalOverlay.BlurRadius, Assert.IsAssignableFrom<IBlurEffect>(backdrop.Effect).Radius, 1);
        ModalOverlay overlay = settings.FindAncestorOfType<ModalOverlay>()!;
        Assert.Equal(backdrop.TranslatePoint(default, ui.Window)!.Value.Y, overlay.TranslatePoint(default, ui.Window)!.Value.Y);
        Assert.False(menus.IsEnabled);
        Assert.Equal(1, overlay.Opacity, 3);

        // It is sized with the window, and follows it when the window is resized; it stays centred.
        Assert.Equal(ModalOverlay.SizeFor(settings, overlay.Bounds.Size), settings.Bounds.Size);
        Size before = settings.Bounds.Size;
        ui.Window.Width = 1700;
        ui.Window.Height = 1000;
        Pump(50);
        Assert.Equal(ModalOverlay.SizeFor(settings, overlay.Bounds.Size), settings.Bounds.Size);
        Assert.True(settings.Bounds.Width > before.Width && settings.Bounds.Height > before.Height);
        Assert.Equal(Math.Floor((overlay.Bounds.Width - settings.Bounds.Width) / 2 + 0.5), settings.Bounds.X);
        ui.Window.Width = 1360;
        ui.Window.Height = 840;
        Pump(50);

        // Esc dismisses it and undoes all of that.
        Key(ui.Window, PhysicalKey.Escape);
        Pump(300);
        Assert.False(settings.IsOpen);
        Assert.False(ModalOverlay.IsOpen(ui.Window));
        Assert.Equal(0, Assert.IsAssignableFrom<IBlurEffect>(backdrop.Effect).Radius, 1);
        Assert.True(menus.IsEnabled);
        Assert.DoesNotContain(overlay, ui.Window.GetVisualDescendants());

        // It opens again as it was left, and About and the shortcuts are sheets too.
        menus.OpenSettings();
        Pump(50);
        Assert.Same(settings, menus.Settings);
        Assert.True(settings.IsOpen);
        settings.Close();
        Pump(250);
    }

    [AvaloniaFact]
    public void TabNavigationStaysInsideTheOpenModal()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump(250);
        SettingsDialog settings = menus.Settings!;
        ModalOverlay overlay = settings.FindAncestorOfType<ModalOverlay>()!;
        bool Inside() => ui.Window.FocusManager!.GetFocusedElement() is Visual focused
            && (focused == overlay || overlay.IsVisualAncestorOf(focused));

        // Forwards past the last control and backwards past the first, focus never reaches the explorer.
        var seen = new HashSet<object>();
        for (int i = 0; i < 40; i++)
        {
            Key(ui.Window, PhysicalKey.Tab);
            Assert.True(Inside(), $"Tab {i} left the modal");
            seen.Add(ui.Window.FocusManager!.GetFocusedElement()!);
        }
        for (int i = 0; i < 40; i++)
        {
            Key(ui.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(Inside(), $"Shift+Tab {i} left the modal");
        }
        // And it does move between the sheet's controls rather than sticking on one.
        Assert.True(seen.Count > 3);
        settings.Close();
        Pump(250);
    }

    [AvaloniaFact]
    public void SettingsEditsTheGamesAndTheStartup()
    {
        string legacy = MakeGame("Legacy");
        HikariHopper.Core.AppSettings.SetStringList(HikariHopper.Core.GameSettings.RecentGameRootsKey, [legacy]);
        Explorer ui = OpenExplorer(openGame: false);
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump(200);
        SettingsDialog settings = menus.Settings!;

        // One row per edition, in the catalogue's order, each with its own executable.
        Assert.Equal([("Enhanced", "GTA5_Enhanced.exe"), ("Legacy", "GTA5.exe")],
            settings.Rows.Select(row => (row.Title, row.ExpectedExecutable)));
        GamePathRow legacyRow = settings.Rows[1];
        Assert.Equal("Not set", legacyRow.StatusMessage);

        // A folder typed in is saved and checked; the clear mark forgets it.
        legacyRow.PathField.Text = legacy;
        legacyRow.PathField.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Views.Controls.FlatTextField.EditingFinishedEvent));
        Pump();
        Assert.Equal(legacy, ui.Tabs.GamePathSettings.LegacyPath);
        Assert.Equal("✓ GTA5.exe found", legacyRow.StatusMessage);
        legacyRow.ClearButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.Equal("", ui.Tabs.GamePathSettings.LegacyPath);
        Assert.Equal("Not set", legacyRow.StatusMessage);

        // Startup: reopening the last game is on until it is turned off, and then nothing reopens.
        HikariHopper.Core.AppSettings.SetString(HikariHopper.Core.GameSettings.LastGameRootKey, legacy);
        Assert.True(settings.OpenLastGame.IsChecked);
        settings.OpenLastGame.IsChecked = false;
        Assert.False(HikariHopper.Core.GameSettings.OpenLastGameOnStartup);
        Assert.Equal("", HikariHopper.Core.GameSettings.GameRootToRestore());
        ui.Bridge.RestoreLastGame();
        Assert.False(ui.Bridge.HasWorkspace);
        settings.OpenLastGame.IsChecked = true;
        Assert.Equal(legacy, HikariHopper.Core.GameSettings.GameRootToRestore());

        // The recents the launcher shows can be cleared.
        Assert.Equal("1 recent game on the start page", settings.RecentText.Text);
        settings.ClearRecents.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.Empty(ui.Tabs.RecentGames);
        Assert.Equal("No recent games", settings.RecentText.Text);
        Assert.False(settings.ClearRecents.IsEnabled);
        settings.Close();
        Pump(250);
    }

    [AvaloniaFact]
    public void ClosingAWorkspaceReleasesItsViews()
    {
        Explorer ui = OpenExplorer();
        var workspace = Assert.IsType<WorkspaceView>(ui.Find<ExplorerScreen>().Page);
        StatusBar status = ui.Find<StatusBar>();

        ui.Bridge.CloseWorkspace();
        Pump();

        // The discarded page lets go of the bridge, so the bridge's subscribers do not keep it alive.
        Assert.IsType<StartPage>(ui.Find<ExplorerScreen>().Page);
        Assert.Null(workspace.DataContext);
        Assert.Null(status.DataContext);
    }

    [AvaloniaFact]
    public void HighlightsStopShortOfTheScrollbar()
    {
        // Enough folders for both the tree and the list to scroll.
        Explorer ui = OpenExplorer(folders: 40);
        Click(ui.Window, Row(0));
        FolderRow selected = ui.Window.GetVisualDescendants().OfType<FolderRow>().Single(row => row.IsSelected);
        int treeRow = (int)selected.TranslatePoint(new Point(0, 13), ui.Window)!.Value.Y;
        using var frame = ui.Window.CaptureRenderedFrame()!;

        // The 12px scrollbar lane at each panel's right edge, beside the thumb: a highlighted row
        // shows there exactly what a plain row does.
        Assert.Equal(HikariHopper.Ui.Theme.Selection.Color, Pixel(frame, 1336, (int)Row(0).Y));
        Assert.Equal(Pixel(frame, 1342, (int)Row(2).Y), Pixel(frame, 1342, (int)Row(0).Y));
        Assert.Equal(Pixel(frame, 236, treeRow + 3 * 26), Pixel(frame, 236, treeRow));
    }

    [AvaloniaFact]
    public void HiddenFoldersKeepTheirWidthAcrossTabs()
    {
        Explorer ui = OpenExplorer();
        ui.Tabs.NewTab();
        ui.Bridge.OpenGame(Work);
        Pump();
        ui.Bridge.SetFoldersVisible(false);
        Pump();
        ExplorerViewModel second = ui.Bridge;

        // Both tabs hide their folders; switching between them must not save the collapsed width.
        ui.Tabs.ActivateTab(0);
        ui.Bridge.SetFoldersVisible(false);
        Pump();
        ui.Tabs.ActivateTab(1);
        Pump();
        Assert.Same(second, ui.Bridge);
        ui.Bridge.SetFoldersVisible(true);
        Pump();

        Assert.Equal(238, ui.Find<FolderPanel>().Bounds.Width);
    }

    [AvaloniaFact]
    public void TableColumnsFollowThePanelWidth()
    {
        Explorer ui = OpenExplorer();
        EntryTable table = ui.Find<EntryTable>();
        double width = table.Bounds.Width;

        // Rows span the panel 6px in from each side; NAME takes what TYPE, SIZE, the scrollbar lane
        // and the row's own padding leave.
        Assert.Equal(width - 12, table.DataBlockWidth);
        Assert.Equal(width - 12 - 10 - 200 - 120 - 14 - 6, table.NameWidth);
        EntryRow row = table.GetVisualDescendants().OfType<EntryRow>().First();
        Assert.Equal(26, row.Bounds.Height);
        Assert.Equal(width - 12, row.Bounds.Width);
        Assert.Equal(new Point(TableLeft + 6, FirstRowTop), row.TranslatePoint(default, ui.Window));
        Assert.Equal(238, ui.Find<FolderPanel>().Bounds.Width);
        Assert.Equal(1360 - 8 - TableLeft, table.Bounds.Width);
    }

    [AvaloniaFact]
    public void ClickCtrlClickAndShiftClickSelect()
    {
        Explorer ui = OpenExplorer();
        Click(ui.Window, Row(2));
        Assert.Equal(2, ui.Bridge.SelectedIndex);
        Assert.Equal(1, ui.Bridge.SelectionCount);

        Click(ui.Window, Row(4), modifiers: RawInputModifiers.Control);
        Assert.Equal(2, ui.Bridge.SelectionCount);

        Click(ui.Window, Row(6), modifiers: RawInputModifiers.Shift);
        Assert.Equal([4, 5, 6], ui.Bridge.EntriesModel.Select((item, index) => (item, index)).Where(pair => pair.item.Selected).Select(pair => pair.index));
    }

    [AvaloniaFact]
    public void ClickOnASelectedEntryCollapsesTheSelectionOnRelease()
    {
        Explorer ui = OpenExplorer();
        ui.Bridge.SelectAllEntries();
        Pump();
        ui.Window.MouseMove(Row(3));
        ui.Window.MouseDown(Row(3), MouseButton.Left);
        Pump();
        // Still the whole selection while the button is down, so it can be dragged out.
        Assert.Equal(ui.Bridge.EntriesModel.Count, ui.Bridge.SelectionCount);
        ui.Window.MouseUp(Row(3), MouseButton.Left);
        Pump();
        Assert.Equal(1, ui.Bridge.SelectionCount);
        Assert.Equal(3, ui.Bridge.SelectedIndex);
    }

    [AvaloniaFact]
    public void ClickingTheTreeToggleExpandsAndCollapsesAFolder()
    {
        Explorer ui = OpenExplorer();
        Directory.CreateDirectory(Path.Combine(Work, "folder00", "child"));
        ui.Bridge.Refresh();
        Pump();
        TreeItem folder = ui.Bridge.TreeModel.First(item => item.Label == "folder00");
        Assert.True(folder.HasChildren);
        Assert.False(folder.Expanded);
        FolderRow row = ui.Window.GetVisualDescendants().OfType<FolderRow>().First(candidate => candidate.Item == folder);
        // The chevron sits in the 16px toggle cell at 4 + depth × 16.
        Point toggle = row.TranslatePoint(new Point(4 + folder.Depth * 16 + 8, row.Bounds.Height / 2), ui.Window)!.Value;
        Click(ui.Window, toggle);
        Assert.True(ui.Bridge.TreeModel.First(item => item.Label == "folder00").Expanded);
        Assert.Contains(ui.Bridge.TreeModel, item => item.Label == "child");
        // A quick second click on the chevron collapses it again; the toggle has no double-click.
        Click(ui.Window, toggle);
        Assert.False(ui.Bridge.TreeModel.First(item => item.Label == "folder00").Expanded);
        Assert.DoesNotContain(ui.Bridge.TreeModel, item => item.Label == "child");
    }

    [AvaloniaFact]
    public void LongTreeLabelsElideInsideTheirHighlight()
    {
        Explorer ui = OpenExplorer(folders: 40);
        const string name = "a folder whose name is far too long for the tree";
        Directory.CreateDirectory(Path.Combine(Work, name));
        ui.Bridge.Refresh();
        Pump();
        ui.Bridge.FocusTreePath(name);
        Pump();
        FolderPanel panel = ui.Find<FolderPanel>();
        FolderRow row = ui.Window.GetVisualDescendants().OfType<FolderRow>().First(candidate => candidate.Item?.Label == name);
        Assert.Equal(1, row.Item!.Depth);
        Assert.True(panel.ScrollbarOverlap > 0);

        // The label ends 8px inside the highlight, which stops at the scrollbar, and elides there.
        double highlightRight = row.Bounds.Width - panel.ScrollbarOverlap;
        Assert.Equal(highlightRight - 8, row.Label.Bounds.Right, 3);
        Assert.True(row.Label.TextLayout.TextLines[0].HasCollapsed);
    }

    [AvaloniaFact]
    public void BackToBackDoubleClicksOpenNestedFolders()
    {
        // Avalonia keeps counting presses (3, 4, …) while they stay close in time and place; like
        // Windows, every second press of the sequence is a double-click.
        Explorer ui = OpenExplorer();
        Directory.CreateDirectory(Path.Combine(Work, "folder00", "inner"));
        ui.Bridge.Refresh();
        Pump();
        Point point = Row(0, TableLeft + 40);
        ui.Window.MouseMove(point);
        for (int press = 0; press < 4; press++)
        {
            ui.Window.MouseDown(point, MouseButton.Left);
            Pump();
            ui.Window.MouseUp(point, MouseButton.Left);
            Pump();
        }
        Assert.Equal("folder00/inner", ui.Bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void DoubleClickOpensAFolder()
    {
        Explorer ui = OpenExplorer();
        Assert.Equal("folder00", ui.Bridge.EntriesModel[0].Name);
        ui.Window.MouseMove(Row(0));
        ui.Window.MouseDown(Row(0), MouseButton.Left);
        ui.Window.MouseUp(Row(0), MouseButton.Left);
        ui.Window.MouseDown(Row(0), MouseButton.Left);
        ui.Window.MouseUp(Row(0), MouseButton.Left);
        Pump();
        Assert.Equal("folder00", ui.Bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void BackgroundClickClearsAndDragDrawsAMarquee()
    {
        // Ten files, the game executable and two folders: thirteen rows; the panel below them is background.
        Explorer ui = OpenExplorer(files: 10);
        EntryTable table = ui.Find<EntryTable>();
        ui.Bridge.SelectEntry(0);
        double background = Row(14).Y;

        Click(ui.Window, new Point(500, background));
        Assert.Equal(0, ui.Bridge.SelectionCount);

        // The panel's inset beside the rows is background too.
        ui.Bridge.SelectEntry(0);
        Click(ui.Window, new Point(TableLeft + 3, Row(1).Y));
        Assert.Equal(0, ui.Bridge.SelectionCount);

        // A drag that starts below the rows selects the rows the band crosses.
        ui.Window.MouseMove(new Point(500, background));
        ui.Window.MouseDown(new Point(500, background), MouseButton.Left);
        ui.Window.MouseMove(new Point(503, background - 3), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.False(table.MarqueeActive);
        ui.Window.MouseMove(new Point(700, Row(8).Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.True(table.MarqueeActive);
        Assert.True(table.MarqueeVisual.IsVisible);
        Assert.Equal(5, ui.Bridge.SelectionCount);
        ui.Window.MouseUp(new Point(700, Row(8).Y), MouseButton.Left);
        Pump();
        Assert.False(table.MarqueeVisual.IsVisible);
        Assert.Equal([8, 9, 10, 11, 12], ui.Bridge.EntriesModel.Select((item, index) => (item, index)).Where(pair => pair.item.Selected).Select(pair => pair.index));
    }

    [AvaloniaFact]
    public void ArrowKeysMoveAndShiftExtendTheSelection()
    {
        Explorer ui = OpenExplorer();
        Click(ui.Window, Row(1));
        Key(ui.Window, PhysicalKey.ArrowDown);
        Assert.Equal(2, ui.Bridge.SelectedIndex);
        Key(ui.Window, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Key(ui.Window, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.Equal(4, ui.Bridge.SelectedIndex);
        Assert.Equal(3, ui.Bridge.SelectionCount);
        Key(ui.Window, PhysicalKey.ArrowUp);
        Assert.Equal(1, ui.Bridge.SelectionCount);
        Assert.Equal(3, ui.Bridge.SelectedIndex);
        Key(ui.Window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Equal(ui.Bridge.EntriesModel.Count, ui.Bridge.SelectionCount);
    }

    [AvaloniaFact]
    public void KeysDoNothingUntilTheListHasFocus()
    {
        Explorer ui = OpenExplorer();
        Key(ui.Window, PhysicalKey.ArrowDown);
        Assert.Equal(-1, ui.Bridge.SelectedIndex);
    }

    [AvaloniaFact]
    public void EnterOpensTheCurrentFolder()
    {
        Explorer ui = OpenExplorer();
        Click(ui.Window, Row(1));
        Key(ui.Window, PhysicalKey.Enter);
        Assert.Equal("folder01", ui.Bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void HeaderClicksSortAndReverse()
    {
        Explorer ui = OpenExplorer(files: 3);
        EntryTable table = ui.Find<EntryTable>();
        Point size = new(TableLeft + 16 + table.NameWidth + 200 + 60, 143);
        Click(ui.Window, size);
        Assert.Equal("size", ui.Bridge.SortColumn);
        Assert.True(ui.Bridge.SortAscending);
        Click(ui.Window, size);
        Assert.False(ui.Bridge.SortAscending);
        Assert.Equal("file000.txt", ui.Bridge.EntriesModel[^1].Name);
        Click(ui.Window, new Point(TableLeft + 40, 143));
        Assert.Equal("name", ui.Bridge.SortColumn);
    }

    [AvaloniaFact]
    public void RightPressOpensTheContextMenuOnTheEntry()
    {
        Explorer ui = OpenExplorer();
        WorkspaceView workspace = ui.Find<WorkspaceView>();
        ui.Window.MouseMove(Row(3));
        ui.Window.MouseDown(Row(3), MouseButton.Right);
        Pump(30);
        Assert.True(workspace.EntryContextMenu.IsOpen, "open on press");
        ui.Window.MouseUp(Row(3), MouseButton.Right);
        Pump(200);
        // The release over the menu that just appeared neither closes it nor triggers an item.
        Assert.True(workspace.EntryContextMenu.IsOpen, "open after release");
        // It is laid out where the press was, in the window's overlay.
        Assert.True(workspace.EntryContextMenu.Bounds.Width >= 230);
        Assert.Equal(Row(3), workspace.EntryContextMenu.TranslatePoint(default, ui.Window));
        Assert.Equal(3, ui.Bridge.SelectedIndex);
        Assert.Equal(".", ui.Bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void RightPressOnTheBackgroundClearsAndOpensTheMenu()
    {
        Explorer ui = OpenExplorer();
        ui.Bridge.SelectEntry(1);
        Click(ui.Window, new Point(1300, 700), MouseButton.Right);
        Assert.True(ui.Find<WorkspaceView>().EntryContextMenu.IsOpen);
        Assert.Equal(0, ui.Bridge.SelectionCount);
    }

    [AvaloniaFact]
    public void DeleteKeyAsksForConfirmationAndEscapeCancels()
    {
        Explorer ui = OpenExplorer();
        Click(ui.Window, Row(3));
        Key(ui.Window, PhysicalKey.Delete);
        WorkspaceView workspace = ui.Find<WorkspaceView>();
        DeleteEntriesDialog dialog = Assert.IsType<DeleteEntriesDialog>(workspace.DeleteDialog);
        Assert.Equal($"Are you sure you want to delete “{ui.Bridge.SelectedName}”?", dialog.Question);
        Assert.True(ModalOverlay.IsOpen(ui.Window));

        // The modal blocks the window's shortcuts.
        int tabs = ui.Tabs.Tabs.Count;
        Key(ui.Window, PhysicalKey.T, RawInputModifiers.Control);
        Assert.Equal(tabs, ui.Tabs.Tabs.Count);

        Key(ui.Window, PhysicalKey.Escape);
        Assert.False(ModalOverlay.IsOpen(ui.Window));
        Assert.True(File.Exists(Path.Combine(Work, ui.Bridge.SelectedName)));
    }

    [AvaloniaFact]
    public void CreateDialogValidatesAndCreatesAFolder()
    {
        Explorer ui = OpenExplorer();
        WorkspaceView workspace = ui.Find<WorkspaceView>();
        workspace.OpenCreateDialog("folder", "New folder", "");
        Pump();
        ExplorerCreateDialog dialog = Assert.IsType<ExplorerCreateDialog>(workspace.CreateDialog);
        Assert.Equal("NEW FOLDER", dialog.Heading);
        Assert.Equal(24 + 112 + 58, dialog.Height);
        Assert.Equal("New folder", dialog.Field.SelectedText);

        dialog.Field.Text = "bad:name";
        Pump();
        Assert.NotEqual("", dialog.NameError);
        Assert.Equal(24 + 112 + 22 + 58, dialog.Height);

        dialog.Field.Text = "made";
        Pump();
        Assert.Equal("", dialog.NameError);
        Assert.True(dialog.Field.IsFocused, "name field focused");
        Key(ui.Window, PhysicalKey.Enter);
        Assert.False(ModalOverlay.IsOpen(ui.Window), ui.Bridge.Status);
        Settle(ui.Bridge);
        Pump();
        Assert.True(Directory.Exists(Path.Combine(Work, "made")), ui.Bridge.Status);
    }

    [AvaloniaFact]
    public void WindowShortcutsActAndYieldToTheSearchField()
    {
        Explorer ui = OpenExplorer();
        ui.Bridge.Navigate("folder00");
        Pump();
        NavigationBar navigation = ui.Find<NavigationBar>();
        TextBox search = navigation.FindControl<TextBox>("SearchField")!;

        Key(ui.Window, PhysicalKey.F3);
        Assert.True(search.IsFocused);
        ui.Window.KeyTextInput("ab");
        Pump();
        Assert.Equal("ab", ui.Bridge.SearchQuery);
        // Backspace edits the field instead of going up.
        Key(ui.Window, PhysicalKey.Backspace);
        Assert.Equal("a", ui.Bridge.SearchQuery);
        Assert.Equal("folder00", ui.Bridge.CurrentPath);
        Assert.Equal("Esc", search.GetValue(Views.Controls.FlatTextField.KeyCapProperty));

        // Escape clears it and gives the focus up; then Backspace goes up.
        Key(ui.Window, PhysicalKey.Escape);
        Assert.Equal("", ui.Bridge.SearchQuery);
        Assert.False(search.IsFocused);
        Key(ui.Window, PhysicalKey.Backspace);
        Assert.Equal(".", ui.Bridge.CurrentPath);

        Key(ui.Window, PhysicalKey.T, RawInputModifiers.Control);
        Assert.Equal(2, ui.Tabs.Tabs.Count);
        Assert.Equal(1, ui.Tabs.ActiveIndex);
        Key(ui.Window, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Single(ui.Tabs.Tabs);
    }

    [AvaloniaFact]
    public void SearchMarksTheMatchedCharacters()
    {
        Explorer ui = OpenExplorer();
        ui.Bridge.SetSearch("LE00");
        Pump();
        EntryRow row = ui.Find<EntryTable>().GetVisualDescendants().OfType<EntryRow>().First();
        TextBlock name = row.GetVisualChildren().OfType<TextBlock>().First();
        var runs = name.Inlines!.OfType<Run>().ToList();
        Assert.Equal(["fi", "le00", "0.txt"], runs.Select(run => run.Text));
        Assert.Equal(FontWeight.Bold, runs[1].FontWeight);
        Assert.Equal(HikariHopper.Ui.Theme.Brass, runs[1].Foreground);
        Assert.Equal(TextDecorations.Underline, runs[1].TextDecorations);
    }

    [AvaloniaFact]
    public void TreeClickFocusesARowAndArrowsMoveTheFocus()
    {
        Explorer ui = OpenExplorer();
        // Tree rows: game root, folder00, folder01.
        Click(ui.Window, new Point(120, TreeTop + 26 + 13));
        Assert.Equal(1, ui.Bridge.TreeFocusIndex);
        Key(ui.Window, PhysicalKey.ArrowDown);
        Assert.Equal(2, ui.Bridge.TreeFocusIndex);
        Key(ui.Window, PhysicalKey.Enter);
        Assert.Equal("folder01", ui.Bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void GridArrowKeysMoveByCellsAndRows()
    {
        Explorer ui = OpenExplorer(files: 30);
        ui.Bridge.SetViewMode("grid");
        Pump();
        EntryGrid grid = ui.Find<EntryGrid>();
        int columns = grid.ColumnsVisible;
        Assert.Equal(Math.Max(1, (int)Math.Floor((grid.Bounds.Width - 12 - 20) / 148)), columns);
        // First cell: 10px content inset, the top margin scrolled away (contentY 0).
        Click(ui.Window, new Point(TableLeft + 10 + 74, GridTop + 56));
        Assert.Equal(0, ui.Bridge.SelectedIndex);
        Key(ui.Window, PhysicalKey.ArrowRight);
        Assert.Equal(1, ui.Bridge.SelectedIndex);
        Key(ui.Window, PhysicalKey.ArrowDown);
        Assert.Equal(1 + columns, ui.Bridge.SelectedIndex);
        Key(ui.Window, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.Equal(2, ui.Bridge.SelectionCount);
    }

    [AvaloniaFact]
    public void GridMarqueeSelectsTheCellsItCrosses()
    {
        Explorer ui = OpenExplorer(files: 30);
        ui.Bridge.SetViewMode("grid");
        Pump();
        EntryGrid grid = ui.Find<EntryGrid>();
        double right = TableLeft + 10 + grid.ColumnsVisible * 148 + 4;
        ui.Window.MouseMove(new Point(right, GridTop + 40));
        ui.Window.MouseDown(new Point(right, GridTop + 40), MouseButton.Left);
        ui.Window.MouseMove(new Point(TableLeft + 10 + 148 + 160, GridTop + 120), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.True(grid.MarqueeActive);
        var expected = GridMath.IndicesInRect(ui.Bridge.EntriesModel.Count, grid.ColumnsVisible, 148, 112, 10, 10,
            TableLeft + 10 + 148 + 160 - TableLeft - 10, right - TableLeft - 10, 40, 120);
        Assert.Equal(expected.Count, ui.Bridge.SelectionCount);
        ui.Window.MouseUp(new Point(TableLeft + 300, GridTop + 120), MouseButton.Left);
    }

    [AvaloniaFact]
    public void GridMathMatchesTheScript()
    {
        Assert.Equal(-1, GridMath.NavigationTarget(0, 0, 4, "down"));
        Assert.Equal(0, GridMath.NavigationTarget(-1, 10, 4, "left"));
        Assert.Equal(9, GridMath.NavigationTarget(8, 10, 4, "down"));
        Assert.Equal(4, GridMath.NavigationTarget(0, 10, 4, "down"));
        Assert.Equal([0, 1, 4, 5], GridMath.IndicesInRect(10, 4, 100, 100, 10, 10, 10, 200, 10, 200));
        Assert.Empty(GridMath.IndicesInRect(10, 4, 100, 100, 10, 10, 500, 600, 10, 200));
        Assert.Equal([8, 9], GridMath.IndicesInRect(10, 4, 100, 100, 10, 10, 10, 400, 250, 300));
    }

    [AvaloniaFact]
    public void EntryTextMarksTheFirstCaseInsensitiveMatch()
    {
        Assert.Equal((2, 3), EntryText.Match("x64a.rpf", "4A."));
        Assert.Equal((-1, 0), EntryText.Match("x64a.rpf", ""));
        Assert.Equal((-1, 0), EntryText.Match("x64a.rpf", "zz"));
    }

    [AvaloniaFact]
    public void LoadingViewAppearsOnly200MsIntoALoad()
    {
        Explorer ui = OpenExplorer(openGame: false);
        using var release = new ManualResetEventSlim();
        LoadHook = path =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return FakeLoadedGame(path);
        };
        ExplorerViewModel.GameLoader = path => LoadHook!(path);
        GameLoadingView loading = ui.Find<GameLoadingView>();
        ui.Bridge.OpenGameAsync(Work);
        Pump(60);
        Assert.False(loading.IsVisible);
        Pump(300);
        Assert.True(ui.Bridge.GameLoading, "loading");
        Assert.True(loading.Revealed, "revealed");
        Assert.True(loading.IsVisible);
        release.Set();
        Settle(ui.Bridge);
        Pump();
        Assert.False(loading.IsVisible);
    }

    [AvaloniaFact]
    public void TabStripActivatesAndClosesTabs()
    {
        Explorer ui = OpenExplorer();
        ui.Tabs.NewTab();
        Pump();
        TabStripTab[] tabs = ui.Window.GetVisualDescendants().OfType<TabStripTab>().ToArray();
        Assert.Equal(2, tabs.Length);
        Assert.Equal(220, tabs[1].Bounds.Width);
        Click(ui.Window, tabs[0].TranslatePoint(new Point(30, 12), ui.Window)!.Value);
        Assert.Equal(0, ui.Tabs.ActiveIndex);
        Click(ui.Window, tabs[1].TranslatePoint(new Point(tabs[1].Bounds.Width - 14, 12), ui.Window)!.Value);
        Assert.Single(ui.Tabs.Tabs);
        Assert.Single(ui.Session.TextureEditors);
    }

    [AvaloniaFact]
    public void MovingATabKeepsTheActiveTab()
    {
        var tabs = new ExplorerTabsViewModel();
        ExplorerViewModel first = tabs.Tabs[0];
        tabs.NewTab();
        tabs.NewTab();
        ExplorerViewModel active = tabs.ActiveBridge;
        tabs.MoveTab(0, 2);
        Assert.Same(first, tabs.Tabs[2]);
        Assert.Same(active, tabs.ActiveBridge);
        Assert.Equal(1, tabs.ActiveIndex);
        tabs.MoveTab(1, 0);
        Assert.Equal(0, tabs.ActiveIndex);
        Assert.Same(active, tabs.ActiveBridge);
        tabs.MoveTab(0, 5);
        Assert.Same(active, tabs.Tabs[0]);
    }

    [AvaloniaFact]
    public void DraggingATabSlidesTheOthersAsideAndDropsItInPlace()
    {
        Explorer ui = OpenExplorer();
        ui.Tabs.NewTab();
        ui.Tabs.NewTab();
        ui.Tabs.ActivateTab(1);
        Pump(100);
        TabStrip strip = ui.Find<TabStrip>();
        ExplorerViewModel[] before = [.. ui.Tabs.Tabs];
        TabStripTab Drawn(ExplorerViewModel tab) =>
            ui.Window.GetVisualDescendants().OfType<TabStripTab>().Single(candidate => candidate.Tab == tab);
        double Offset(ExplorerViewModel tab) => TabStrip.SlideOffset((Control)Drawn(tab).Parent!);
        Point start = Drawn(before[0]).TranslatePoint(new Point(60, 16), ui.Window)!.Value;

        // A press alone, or a few pixels of jitter, is still a click.
        ui.Window.MouseMove(start);
        ui.Window.MouseDown(start, MouseButton.Left);
        ui.Window.MouseMove(start + new Point(3, 0), RawInputModifiers.LeftMouseButton);
        Assert.False(strip.Dragging);

        // Past the threshold the tab activates and follows the pointer; the tabs it passes slide aside.
        ui.Window.MouseMove(start + new Point(100, 0), RawInputModifiers.LeftMouseButton);
        Assert.True(strip.Dragging);
        Assert.Same(before[0], ui.Tabs.ActiveBridge);
        Assert.Equal(100, Offset(before[0]), 3);
        ui.Window.MouseMove(start + new Point(350, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(350, Offset(before[0]), 3);
        Pump(20);
        double sliding = Offset(before[1]);
        Assert.InRange(sliding, -219.9, -0.1);  // on its way, not jumped
        Pump(300);
        Assert.Equal(-220, Offset(before[1]), 3);
        Assert.Equal(-220, Offset(before[2]), 3);

        // It can't leave the strip.
        ui.Window.MouseMove(start + new Point(900, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(440, Offset(before[0]), 3);

        // Dropped past the last tab's middle, it lands last and eases the rest of the way in.
        ui.Window.MouseMove(start + new Point(400, 0), RawInputModifiers.LeftMouseButton);
        ui.Window.MouseUp(start + new Point(400, 0), MouseButton.Left);
        Assert.False(strip.Dragging);
        Assert.Equal([before[1], before[2], before[0]], ui.Tabs.Tabs);
        Assert.Equal(2, ui.Tabs.ActiveIndex);
        Assert.Same(before[0], ui.Tabs.ActiveBridge);
        Assert.Equal(0, Offset(before[1]), 3);
        Assert.Equal(0, Offset(before[2]), 3);
        Assert.InRange(Offset(before[0]), -40.1, 0);
        Pump(300);
        Assert.Equal(0, Offset(before[0]), 3);
        Assert.Equal(new Point(14 + 440, 42), Drawn(before[0]).TranslatePoint(default, ui.Window)!.Value);
        // The session still has one texture window per tab.
        Assert.Equal(3, ui.Session.TextureEditors.Count);
        Assert.True(ui.Session.TextureEditors.ContainsKey(before[0]));
    }

    [AvaloniaFact]
    public void ClosingATabGlidesTheRestIntoPlace()
    {
        Explorer ui = OpenExplorer();
        ui.Tabs.NewTab();
        // The tab that is left at the end has a game, so the strip stays: a lone empty tab hides it.
        ui.Tabs.ActiveBridge.OpenGame(Work);
        ui.Tabs.NewTab();
        Pump(100);
        TabStrip strip = ui.Find<TabStrip>();
        ExplorerViewModel[] before = [.. ui.Tabs.Tabs];
        TabStripTab Drawn(ExplorerViewModel tab) =>
            ui.Window.GetVisualDescendants().OfType<TabStripTab>().Single(candidate => candidate.Tab == tab);
        double Offset(ExplorerViewModel tab) => TabStrip.SlideOffset((Control)Drawn(tab).Parent!);
        Control plus = strip.FindControl<Control>("NewTabButton")!;
        double plusLeft = plus.TranslatePoint(default, ui.Window)!.Value.X;

        // Closing the first tab: the others start where they were drawn and glide one tab left.
        TabStripTab first = Drawn(before[0]);
        Click(ui.Window, first.TranslatePoint(new Point(first.Bounds.Width - 14, 16), ui.Window)!.Value);
        Assert.Equal([before[1], before[2]], ui.Tabs.Tabs);
        Pump(20);
        Assert.InRange(Offset(before[1]), 0.1, 219.9);
        Assert.InRange(Offset(before[2]), 0.1, 219.9);
        Assert.InRange(TabStrip.SlideOffset(plus), 0.1, 219.9);
        Pump(300);
        Assert.Equal(0, Offset(before[1]), 3);
        Assert.Equal(0, Offset(before[2]), 3);
        Assert.Equal(0, TabStrip.SlideOffset(plus), 3);
        Assert.Equal(new Point(14, 42), Drawn(before[1]).TranslatePoint(default, ui.Window)!.Value);
        Assert.Equal(plusLeft - 220, plus.TranslatePoint(default, ui.Window)!.Value.X, 3);

        // Closing the last one moves nothing but the button.
        TabStripTab last = Drawn(before[2]);
        Click(ui.Window, last.TranslatePoint(new Point(last.Bounds.Width - 14, 16), ui.Window)!.Value);
        Pump(20);
        Assert.Equal(0, Offset(before[1]), 3);
        Assert.InRange(TabStrip.SlideOffset(plus), 0.1, 219.9);
        Pump(300);
        Assert.Equal(plusLeft - 440, plus.TranslatePoint(default, ui.Window)!.Value.X, 3);
    }

    [AvaloniaFact]
    public void GameTabsShowTheirEditionAndTellTheirPath()
    {
        Explorer ui = OpenExplorer();
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        ui.Tabs.NewTab();
        ui.Tabs.ActiveBridge.OpenGame(enhanced);
        ui.Tabs.NewTab();
        Pump();
        TabStripTab[] tabs = ui.Window.GetVisualDescendants().OfType<TabStripTab>().ToArray();
        Assert.Equal(3, tabs.Length);

        // A grey V for Legacy, a green one for Enhanced, in place of the folder glyph.
        Assert.True(tabs[0].EditionMark.IsVisible);
        Assert.False(tabs[0].Glyph.IsVisible);
        Assert.Equal(HikariHopper.Ui.Theme.TextDim, tabs[0].EditionMark.Fill);
        Assert.True(tabs[1].EditionMark.IsVisible);
        Assert.Equal(HikariHopper.Ui.Theme.Selection, tabs[1].EditionMark.Fill);
        // The tooltip is just the path.
        Assert.Equal(enhanced, tabs[1].TipText);
        Assert.Equal(Work.TrimEnd(Path.DirectorySeparatorChar), tabs[0].TipText);

        // A new tab keeps its folder and has nothing to tell.
        Assert.False(tabs[2].EditionMark.IsVisible);
        Assert.True(tabs[2].Glyph.IsVisible);
        Assert.Equal("", tabs[2].TipText);
        Assert.False(ToolTip.GetServiceEnabled(tabs[2].TipHost));
        Assert.True(ToolTip.GetServiceEnabled(tabs[1].TipHost));

        // Opening a game there swaps the glyph for its mark.
        ui.Tabs.ActiveBridge.OpenGame(enhanced);
        Pump();
        Assert.True(tabs[2].EditionMark.IsVisible);
        Assert.Equal(HikariHopper.Ui.Theme.Selection, tabs[2].EditionMark.Fill);

        // The V itself is drawn in its colour: solid where its arms meet, open between them above.
        using var frame = ui.Window.CaptureRenderedFrame()!;
        Point Inside(double x, double y) => tabs[1].EditionMark.TranslatePoint(new Point(x, y), ui.Window)!.Value;
        Assert.Equal(HikariHopper.Ui.Theme.Selection.Color, Pixel(frame, (int)Inside(7.5, 10).X, (int)Inside(7.5, 10).Y));
        Assert.NotEqual(HikariHopper.Ui.Theme.Selection.Color, Pixel(frame, (int)Inside(7.5, 4).X, (int)Inside(7.5, 4).Y));
    }

    [AvaloniaFact]
    public void FoldersShowTheirItemCountInTheSizeColumnAndNoType()
    {
        Explorer ui = OpenExplorer(files: 2, folders: 3);
        Write(Path.Combine(Work, "folder00", "a.txt"), "a");
        Write(Path.Combine(Work, "folder00", "b.txt"), "b");
        Write(Path.Combine(Work, "folder01", "c.txt"), "c");
        ui.Bridge.Refresh();
        Pump();
        EntryTable table = ui.Find<EntryTable>();
        TextBlock[] Cells(string name) => table.GetVisualDescendants().OfType<EntryRow>()
            .Single(row => row.Item?.Name == name).GetVisualChildren().OfType<TextBlock>().ToArray();

        // Name, type and size only: the count no longer trails the name.
        TextBlock[] full = Cells("folder00");
        Assert.Equal(3, full.Length);
        Assert.Equal(("", "2 items"), (full[1].Text, full[2].Text));
        Assert.Equal(HikariHopper.Ui.Theme.TextFaint, full[2].Foreground);
        Assert.Equal("1 item", Cells("folder01")[2].Text);
        Assert.Equal(("", "Empty"), (Cells("folder02")[1].Text, Cells("folder02")[2].Text));

        // Files are unchanged.
        EntryItem file = ui.Bridge.EntriesModel.First(item => item.Name == "file001.txt");
        TextBlock[] fileCells = Cells("file001.txt");
        Assert.Equal((file.Kind, file.SizeLabel), (fileCells[1].Text, fileCells[2].Text));
        Assert.Equal(HikariHopper.Ui.Theme.TextRow, fileCells[2].Foreground);

        // The grid cell says the same.
        ui.Bridge.SetViewMode("grid");
        Pump();
        EntryGridCell cell = ui.Window.GetVisualDescendants().OfType<EntryGridCell>().First(candidate => candidate.Item?.Name == "folder02");
        Assert.Equal("Empty", cell.SizeText.Text);
    }

    [AvaloniaFact]
    public void TheTitleRowCarriesTheMenusAndTheChromeHasTheRefreshedMetrics()
    {
        Explorer ui = OpenExplorer();
        ui.Tabs.NewTab();
        ui.Tabs.ActivateTab(0);
        Pump(100);
        Window window = ui.Window;
        Point At(Visual visual) => visual.TranslatePoint(default, window)!.Value;

        // One 38px title row: emblem, menus, empty draggable space and 46px caption buttons.
        Views.Controls.WindowTitleBar bar = ui.Window.TitleBar;
        Assert.Equal(38, bar.Bounds.Height);
        Assert.False(bar.TitleLabel.IsVisible);
        Assert.Equal(46, bar.CloseButton.Bounds.Width);
        MenuRow menus = ui.Find<MenuRow>();
        Assert.Contains(menus, bar.GetVisualDescendants());

        // The tab strip: 32px tabs edge to edge on its floor, the active one meeting the toolbar.
        TabStrip strip = ui.Find<TabStrip>();
        Assert.Equal((new Point(0, 38), 36.0), (At(strip), strip.Bounds.Height));
        TabStripTab[] tabs = window.GetVisualDescendants().OfType<TabStripTab>().ToArray();
        Assert.True(tabs[0].Active);
        Assert.Equal(new Rect(14, 42, 220, 32), new Rect(At(tabs[0]), tabs[0].Bounds.Size));
        Assert.Equal(new Rect(234, 42, 220, 32), new Rect(At(tabs[1]), tabs[1].Bounds.Size));

        NavigationBar navigation = ui.Find<NavigationBar>();
        Assert.Equal((new Point(0, 74), 46.0), (At(navigation), navigation.Bounds.Height));

        // Two panels floating on the base with 8px gutters, and the status bar under them.
        WorkspaceView workspace = ui.Find<WorkspaceView>();
        Assert.Equal(new Rect(8, 128, 238, 672), new Rect(At(workspace.FoldersFrame), workspace.FoldersFrame.Bounds.Size));
        Assert.Equal(new Point(254, 128), At(workspace.EntriesFrame));
        Assert.Equal(new CornerRadius(6), workspace.FoldersFrame.CornerRadius);
        Assert.Equal(new CornerRadius(6), workspace.EntriesFrame.CornerRadius);
        StatusBar status = ui.Find<StatusBar>();
        Assert.Equal((new Point(0, 808), 32.0), (At(status), status.Bounds.Height));

        using var frame = window.CaptureRenderedFrame()!;
        Assert.Equal(HikariHopper.Ui.Theme.WindowBg.Color, Pixel(frame, 4, 400));        // left gutter
        Assert.Equal(HikariHopper.Ui.Theme.WindowBg.Color, Pixel(frame, 250, 400));      // gutter between the panels
        Assert.Equal(HikariHopper.Ui.Theme.SidebarBg.Color, Pixel(frame, 100, 700));     // tree panel
        Assert.Equal(HikariHopper.Ui.Theme.PanelBg.Color, Pixel(frame, 700, 700));       // entries panel
        Assert.NotEqual(HikariHopper.Ui.Theme.PanelBg.Color, Pixel(frame, 254, 128));    // its rounded corner
        Assert.Equal(HikariHopper.Ui.Theme.TabStripBg.Color, Pixel(frame, 1000, 50));
        Assert.Equal(HikariHopper.Ui.Theme.ChromeBg.Color, Pixel(frame, 13, 73));        // the active tab's foot
        Assert.Equal(HikariHopper.Ui.Theme.TabStripBg.Color, Pixel(frame, 7, 67));       // outside its curve
        Assert.Equal(HikariHopper.Ui.Theme.TabStripBg.Color, Pixel(frame, 300, 50));     // a bare inactive tab

        // Hovering an inactive tab raises the same shape a shade lighter.
        window.MouseMove(new Point(300, 58));
        Pump();
        using var hovered = window.CaptureRenderedFrame()!;
        Assert.Equal(HikariHopper.Ui.Theme.TabBg.Color, Pixel(hovered, 300, 50));
        Assert.Equal(HikariHopper.Ui.Theme.TabBg.Color, Pixel(hovered, 456, 73));       // its foot, past its edge
    }

    [AvaloniaFact]
    public void TheTitleRowStillMaximizesAndItsMenusOpenBelowIt()
    {
        Explorer ui = OpenExplorer();
        var empty = new Point(700, 19);
        ui.Window.MouseMove(empty);
        for (int press = 0; press < 2; press++)
        {
            ui.Window.MouseDown(empty, MouseButton.Left);
            ui.Window.MouseUp(empty, MouseButton.Left);
        }
        Pump();
        Assert.Equal(WindowState.Maximized, ui.Window.WindowState);
        ui.Window.WindowState = WindowState.Normal;
        Pump(100);

        MenuRow menus = ui.Find<MenuRow>();
        Views.Controls.MenuBarButton file = menus.FileButton;
        Point center = file.TranslatePoint(new Point(file.Bounds.Width / 2, file.Bounds.Height / 2), ui.Window)!.Value;
        Click(ui.Window, center);
        Views.Controls.RetroMenu menu = menus.MenuFor(file);
        Assert.True(menu.IsOpen);
        Assert.True(file.MenuOpen);
        // The menu hangs from its title: its frame starts at the title's bottom-left corner and
        // leaves the title's width open on top.
        Assert.Equal(file.TranslatePoint(new Point(0, file.Bounds.Height), ui.Window), menu.TranslatePoint(default, ui.Window));
        Assert.Equal(file.Bounds.Width, menu.ConnectionWidth);
        // Its shadow falls below it, over the window, with nothing clipping it.
        Assert.False(menu.ClipToBounds);
        using (var frame = ui.Window.CaptureRenderedFrame()!)
        {
            Point below = menu.TranslatePoint(new Point(menu.Bounds.Width / 2, menu.Bounds.Height + 4), ui.Window)!.Value;
            Point clear = menu.TranslatePoint(new Point(menu.Bounds.Width / 2, menu.Bounds.Height + 40), ui.Window)!.Value;
            Color shaded = Pixel(frame, (int)below.X, (int)below.Y);
            Color plain = Pixel(frame, (int)clear.X, (int)clear.Y);
            Assert.True(shaded.R + shaded.G + shaded.B < plain.R + plain.G + plain.B, $"{shaded} is no darker than {plain}");
        }
        menu.Close();
    }

    [AvaloniaFact]
    public void StatusBarShowsTheEditionLampAndOnlyTheKeysThatMove()
    {
        Explorer ui = OpenExplorer();
        StatusBar status = ui.Find<StatusBar>();
        Assert.Equal("GTA V Legacy", ui.Bridge.GameEdition);
        Assert.True(status.Edition.IsVisible);
        Assert.Equal("GTA V Legacy", status.EditionText.Text);
        // The load confirmation only repeats the lamp.
        Assert.Equal("Loaded GTA V Legacy", ui.Bridge.Status);
        Assert.Equal("", status.VisibleStatus);
        Assert.False(StatusBar.StatusWorthShowing("Loaded GTA V Legacy", "GTA V Legacy"));
        Assert.True(StatusBar.StatusWorthShowing("Dragging one item as a copy", "GTA V Legacy"));
        Assert.True(StatusBar.StatusWorthShowing("Opened sample.rpf", ""));
        Assert.False(StatusBar.StatusWorthShowing("", ""));

        string[] texts = status.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
        Assert.DoesNotContain(texts, text => text.Contains("FiveFury") || text.Contains("Ctrl+O") || text.Contains("F3"));
        var caps = status.GetVisualDescendants().OfType<Views.Controls.KeyCap>().ToArray();
        Assert.Equal(["Enter", ""], caps.Select(cap => cap.Text));
        Assert.Equal("delete", caps[1].IconName);

        // A standalone archive has no edition, so no lamp.
        ui.Bridge.CloseArchive();
        ui.Tabs.NewTab();
        Pump();
        Assert.Equal("", ui.Tabs.ActiveBridge.GameEdition);
    }

    [AvaloniaFact]
    public void ThePaintedPipingStepsAsideForTheNativeBorder()
    {
        Explorer ui = OpenExplorer();
        Views.Controls.WindowPiping piping = ui.Find<Views.Controls.WindowPiping>();
        // Headless there is no DWM, so the square fallback piping is painted.
        Assert.True(piping.IsVisible);
        Views.Controls.WindowPiping.SetNativeBorder(ui.Window, true);
        Assert.False(piping.IsVisible);
    }

    private static Color Pixel(Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y)
    {
        using Avalonia.Platform.ILockedFramebuffer buffer = frame.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private DataTransfer DroppedFile(TopLevel top, string name, bool fromExplorer = false)
    {
        string source = Path.Combine(Directory.CreateDirectory(Path.Combine(Work, "..", "outside")).FullName, name);
        File.WriteAllText(source, "dropped");
        var data = new DataTransfer();
        Avalonia.Platform.Storage.IStorageFile? file = top.StorageProvider.TryGetFileFromPathAsync(new Uri(source)).GetAwaiter().GetResult();
        Assert.NotNull(file);
        data.Add(DataTransferItem.CreateFile(file));
        if (fromExplorer)
            data.Add(DataTransferItem.Create(EntryPointerArea.EntryDragFormat, [(byte)'1']));
        return data;
    }

    [AvaloniaFact]
    public void ExternalFilesDroppedOnTheEntriesAreImported()
    {
        Explorer ui = OpenExplorer();
        ExternalFileDropArea dropArea = ui.Find<ExternalFileDropArea>();
        DataTransfer data = DroppedFile(ui.Window, "dropped.txt");
        var point = new Point(700, 400);
        ui.Window.DragDrop(point, Avalonia.Input.Raw.RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        Pump();
        Assert.True(dropArea.HighlightVisible);
        ui.Window.DragDrop(new Point(500, 300), Avalonia.Input.Raw.RawDragEventType.DragOver, data, DragDropEffects.Copy);
        Pump();
        Assert.True(dropArea.HighlightVisible);
        ui.Window.DragDrop(point, Avalonia.Input.Raw.RawDragEventType.Drop, data, DragDropEffects.Copy);
        Settle(ui.Bridge);
        Pump();
        Assert.False(dropArea.HighlightVisible);
        Assert.True(File.Exists(Path.Combine(Work, "dropped.txt")), ui.Bridge.Status);
    }

    [AvaloniaFact]
    public void TheExplorersOwnDragsAreRefused()
    {
        Explorer ui = OpenExplorer();
        ExternalFileDropArea dropArea = ui.Find<ExternalFileDropArea>();
        DataTransfer data = DroppedFile(ui.Window, "own.txt", fromExplorer: true);
        ui.Window.DragDrop(new Point(700, 400), Avalonia.Input.Raw.RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        Pump();
        Assert.False(dropArea.HighlightVisible);
        Assert.False(dropArea.Accepts(data));
        ui.Window.DragDrop(new Point(700, 400), Avalonia.Input.Raw.RawDragEventType.Drop, data, DragDropEffects.Copy);
        Settle(ui.Bridge);
        Assert.False(File.Exists(Path.Combine(Work, "own.txt")));
    }

    [AvaloniaFact]
    public void DraggingAnEntryStagesItAsACopy()
    {
        Explorer ui = OpenExplorer();
        Point start = Row(3);
        ui.Window.MouseMove(start);
        ui.Window.MouseDown(start, MouseButton.Left);
        ui.Window.MouseMove(new Point(start.X + 6, start.Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.DoesNotContain("Dragging", ui.Bridge.Status);
        ui.Window.MouseMove(new Point(start.X + 12, start.Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.Equal("Dragging one item as a copy", ui.Bridge.Status);
        ui.Window.MouseUp(new Point(start.X + 12, start.Y), MouseButton.Left);
    }

    [AvaloniaFact]
    public void WheelGlidesOneNotchAtATime()
    {
        Explorer ui = OpenExplorer(files: 80);
        ScrollViewer scroller = ui.Find<EntryTable>().FindControl<ScrollViewer>("Scroller")!;
        ui.Window.MouseMove(new Point(600, 400));
        ui.Window.MouseWheel(new Point(600, 400), new Vector(0, -1));
        Pump(30);
        Assert.InRange(scroller.Offset.Y, 1, SmoothScrolling.NotchDistance - 1);
        Pump(400);
        Assert.Equal(SmoothScrolling.NotchDistance, scroller.Offset.Y, 3);
    }

    [AvaloniaFact]
    public void FastWheelTurnsAddUp()
    {
        Explorer ui = OpenExplorer(files: 80);
        ScrollViewer scroller = ui.Find<EntryTable>().FindControl<ScrollViewer>("Scroller")!;
        ui.Window.MouseMove(new Point(600, 400));
        // Notches that arrive mid-glide extend it instead of restarting from where the view is.
        for (int notch = 0; notch < 3; notch++)
        {
            ui.Window.MouseWheel(new Point(600, 400), new Vector(0, -1));
            Pump(15);
        }
        Pump(500);
        Assert.Equal(3 * SmoothScrolling.NotchDistance, scroller.Offset.Y, 3);
    }

    [AvaloniaFact]
    public void WheelAtAnEndStretchesTheListAndSpringsBack()
    {
        Explorer ui = OpenExplorer(files: 80);
        ScrollViewer scroller = ui.Find<EntryTable>().FindControl<ScrollViewer>("Scroller")!;
        Control rows = (Control)scroller.Content!;
        ui.Window.MouseMove(new Point(600, 400));

        ui.Window.MouseWheel(new Point(600, 400), new Vector(0, 1));
        Pump(40);
        Assert.Equal(0, scroller.Offset.Y);
        Assert.True(rows.RenderTransform is Avalonia.Media.TranslateTransform { Y: > 0 }, "The top of the list should stretch down");
        // It springs back in sub-pixel steps; whole-pixel steps look choppy over a few pixels.
        var stretches = new List<double>();
        for (int sample = 0; sample < 20; sample++)
        {
            if (rows.RenderTransform is Avalonia.Media.TranslateTransform translate)
                stretches.Add(translate.Y);
            Pump(15);
        }
        Assert.Contains(stretches, y => Math.Abs(y - Math.Round(y)) > 0.01);
        Pump(600);
        Assert.Null(rows.RenderTransform);

        // The bottom stretches the other way.
        scroller.Offset = scroller.Offset.WithY(scroller.Extent.Height - scroller.Viewport.Height);
        Pump();
        ui.Window.MouseWheel(new Point(600, 400), new Vector(0, -1));
        Pump(40);
        Assert.True(rows.RenderTransform is Avalonia.Media.TranslateTransform { Y: < 0 }, "The bottom of the list should stretch up");
        Pump(600);
        Assert.Null(rows.RenderTransform);
    }

    [AvaloniaFact]
    public void DraggingTheScrollbarTakesOverFromAGlide()
    {
        Explorer ui = OpenExplorer(files: 80);
        ScrollViewer scroller = ui.Find<EntryTable>().FindControl<ScrollViewer>("Scroller")!;
        ui.Window.MouseMove(new Point(600, 400));
        ui.Window.MouseWheel(new Point(600, 400), new Vector(0, -3));
        Pump(20);
        // Pressing the scrollbar thumb (near the top of the lane at the panel's right edge) stops the
        // glide where it is.
        var lane = new Point(1346, 200);
        ui.Window.MouseMove(lane);
        ui.Window.MouseDown(lane, MouseButton.Left);
        Pump(20);
        double held = scroller.Offset.Y;
        Assert.InRange(held, 1, 3 * SmoothScrolling.NotchDistance - 1);
        Pump(400);
        Assert.Equal(held, scroller.Offset.Y, 3);
        ui.Window.MouseUp(lane, MouseButton.Left);
    }

    [AvaloniaFact]
    public void ListFollowsTheCurrentRowLikeQtListView()
    {
        Explorer ui = OpenExplorer(files: 80);
        ScrollViewer scroller = ui.Find<EntryTable>().FindControl<ScrollViewer>("Scroller")!;
        // From no current row the view jumps to show the new one.
        ui.Bridge.SelectEntry(60);
        Pump();
        Assert.Equal(61 * 26 - scroller.Viewport.Height, scroller.Offset.Y, 3);
        // Moving the current row far away glides there (400px/s), so the view has not moved yet.
        ui.Bridge.SelectEntry(5);
        Pump(50);
        Assert.Equal(61 * 26 - scroller.Viewport.Height, scroller.Offset.Y, 3);
    }
}
