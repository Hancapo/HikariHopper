using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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

    private static void Pump() => TestWait.Settle();

    private static void Pump(int milliseconds) => TestWait.Pump(milliseconds);

    /// <summary>
    /// Slows the tabs' slides down for a test, so one caught "midway" a few milliseconds in is still
    /// midway on a busy machine; dispose to restore the app's own speed.
    /// </summary>
    private static IDisposable SlowSlides()
    {
        TimeSpan original = TabStrip.SlideDuration;
        TabStrip.SlideDuration = TimeSpan.FromMilliseconds(SlowSlideMilliseconds);
        return new Restore(() => TabStrip.SlideDuration = original);
    }

    private const int SlowSlideMilliseconds = 600;

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

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
        KeyNow(window, key, modifiers);
        Pump();
    }

    /// <summary>A key pressed and handled, without drawing: for what moves at once, such as focus.</summary>
    private static void KeyNow(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void StartPageShowsUntilAGameIsOpen()
    {
        Explorer ui = OpenExplorer(openGame: false);
        Assert.IsType<StartPage>(ui.Find<ExplorerScreen>().Page);
        Assert.Equal("HikariHopper — Modding Toolkit", ui.Window.Title);

        ui.Bridge.OpenGame(Work);
        Pump();

        Assert.IsType<WorkspaceView>(ui.Find<ExplorerScreen>().Page);
        Assert.Equal($"HikariHopper — {ui.Bridge.TabTitle}", ui.Window.Title);
    }

    [AvaloniaFact]
    public void A_drop_with_names_already_here_asks_whether_to_replace_them()
    {
        Explorer ui = OpenExplorer(files: 3);
        string drops = Directory.CreateDirectory(Path.Combine(Work, "..", "drops-" + Guid.NewGuid().ToString("N"))).FullName;
        string[] dropped = [.. Enumerable.Range(0, 20).Select(index => Write(Path.Combine(drops, $"file{index:000}.txt"), "new"))];

        // Three of the twenty are already here: a dialog asks, and nothing is imported until it's answered.
        Assert.True(ui.Bridge.ImportDroppedFiles(dropped));
        Pump();
        ImportConflictDialog dialog = Assert.IsType<ImportConflictDialog>(ui.Find<WorkspaceView>().ImportDialog);
        Assert.Equal("3 of the 20 items are already here.", dialog.Question);
        Assert.Equal("file000.txt, file001.txt, file002.txt", dialog.Names);
        Assert.False(File.Exists(Path.Combine(Work, "file019.txt")));
        if (Environment.GetEnvironmentVariable("HH_SHOT") is { } shot)
            using (var frame = ui.Window.CaptureRenderedFrame())
                frame?.Save(shot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        // Replace: all twenty go in, the three replaced, and the dialog closes.
        dialog.ReplaceButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (ui.Bridge.EntryOperationBusy && clock.ElapsedMilliseconds < 10_000)
            Pump(20);
        Pump();
        Assert.Null(ui.Find<WorkspaceView>().ImportDialog);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Work, "file000.txt")));
        Assert.True(File.Exists(Path.Combine(Work, "file019.txt")));
        ui.Window.Close();
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
        Pump();

        // A configured edition is a row with its tinted V and its path, and a recent install of the
        // same game joins it under its edition's name (Legacy here); the configured one isn't repeated.
        string enhanced = MakeGame("Enhanced", "GTA5_Enhanced.exe");
        string other = MakeGame("Modded GTA");
        HikariHopper.Core.AppSettings.SetString(HikariHopper.Core.GameSettings.EnhancedGameRootKey, enhanced);
        HikariHopper.Core.AppSettings.SetStringList(HikariHopper.Core.GameSettings.RecentGameRootsKey, [enhanced, other]);
        ui.Tabs.GamePathSettings.Refresh();
        Pump();
        LauncherRow[] rows = [.. start.Rows];
        Assert.Equal([("Enhanced", enhanced), ("Legacy", other)], rows.Select(row => (row.Entry.Title, row.Entry.Path)));
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
    public void AClosingModalShrinksAwayAsAPictureOfItself()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump();
        SettingsDialog settings = menus.Settings!;
        ModalOverlay overlay = settings.FindAncestorOfType<ModalOverlay>()!;
        Rect shown = settings.Bounds;
        // Slowed down, so the close caught midway is still midway on a busy machine.
        TimeSpan speed = ModalOverlay.CloseDuration;
        ModalOverlay.CloseDuration = TimeSpan.FromMilliseconds(1200);
        using var restore = new Restore(() => ModalOverlay.CloseDuration = speed);

        // Closed: the dialog is let go at once, so what it shows can go too, and a picture of it takes its place.
        settings.Close();
        Assert.Null(settings.Parent);
        Assert.False(ModalOverlay.IsOpen(ui.Window));
        Image picture = Assert.Single(overlay.Children.OfType<Image>());
        Pump(10);
        Assert.Equal(shown, picture.Bounds);
        Assert.NotNull(picture.Source);

        // It shrinks toward its centre as it fades, with the dim, and the overlay leaves once it is gone.
        Pump(500);
        Assert.InRange(picture.Opacity, 0.01, 0.99);
        Assert.InRange(overlay.Opacity, 0.01, 0.99);
        Matrix midway = picture.RenderTransform!.Value;
        Assert.InRange(midway.M11, 0.92, 0.999);
        Assert.Equal(midway.M11, midway.M22, 3);
        TestWait.Until(() => !ui.Window.GetVisualDescendants().Contains(overlay), "the closed modal to leave");
        Assert.Null(picture.Source);

        // Shown again straight after closing, it is there as before.
        menus.OpenSettings();
        settings.Close();
        menus.OpenSettings();
        Pump();
        Assert.True(settings.IsOpen);
        Assert.Equal(shown.Size, settings.Bounds.Size);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void SwitchingViewsMorphsOneIntoTheOtherWhereYouWere()
    {
        // Slowed down, so the morph caught midway is still midway on a busy machine; animated whatever Windows says.
        EntryViewMorph.Pace = 4;
        Ui.Motion.ReducedOverride = false;
        using var restore = new Restore(TestMotion.Restore);
        Explorer ui = OpenExplorer(files: 80);
        ExplorerViewModel bridge = ui.Session.Tabs.ActiveBridge;
        WorkspaceView workspace = ui.Find<WorkspaceView>();
        EntryTable table = Assert.IsType<EntryTable>(workspace.EntriesView);
        table.ScrollViewer.Offset = new Vector(0, 30 * Ui.Theme.RowHeight);
        Pump();

        // The grid comes in where the list was: the row with the first item you could see, at the top.
        bridge.SetViewMode("grid");
        Pump(20);
        EntryGrid grid = Assert.IsType<EntryGrid>(workspace.EntriesView);
        int columns = grid.ColumnsVisible;
        Assert.Equal(Ui.Theme.GridContentInset + 30 / columns * Ui.Theme.GridCellHeight, grid.ScrollViewer.Offset.Y, 1);
        // The items glide from where they were, the old list fading out beneath as a picture of itself.
        EntryGridCell[] cells = [.. grid.RealizedCells().Where(cell => cell.Item is not null)];
        Assert.Contains(cells, cell => cell.RenderTransform is { } transform && !transform.Value.IsIdentity);
        Assert.Contains(cells, cell => cell.NameText.Opacity < 1);
        Assert.Single(workspace.EntriesArea.Children.OfType<Image>());

        // Once there, nothing is left over: containers are reused as the grid scrolls.
        TestWait.Until(() => !workspace.EntriesArea.Children.OfType<Image>().Any()
            && grid.RealizedCells().All(cell => cell.RenderTransform is null && cell.Transitions is null), "the morph to end");
        Assert.All(grid.RealizedCells(), cell =>
        {
            Assert.Null(cell.RenderTransform);
            Assert.Equal(1, cell.Opacity);
            Assert.Equal(1, cell.NameText.Opacity);
            Assert.Null(cell.Transitions);
        });
        Assert.Empty(workspace.EntriesArea.Children.OfType<Image>());

        // With Windows' animation effects off, the switch is at once.
        Ui.Motion.ReducedOverride = true;
        bridge.SetViewMode("list");
        Pump(20);
        table = Assert.IsType<EntryTable>(workspace.EntriesView);
        Assert.Empty(workspace.EntriesArea.Children.OfType<Image>());
        Assert.All(table.GetVisualDescendants().OfType<EntryRow>(), row => Assert.Null(row.RenderTransform));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void SettingsOpensAsAModalOverTheBlurredExplorer()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump();
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
        Pump();
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
        Pump();
    }

    [AvaloniaFact]
    public void AboutsChangelogOpensOverItAndEscapeComesBack()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        var about = new AboutDialog();
        about.Present(menus);
        Pump();
        about.FindControl<Views.Controls.ChromeToolButton>("ChangelogButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        ChangelogDialog changelog = about.Changelog!;
        Assert.True(changelog.IsOpen);
        Assert.True(about.IsOpen);
        Assert.Equal("CHANGELOG", changelog.Heading);
        // Every version in the file, its groups and entries shown.
        Assert.Contains(changelog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "0.0.2");
        Assert.Contains(changelog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "FIXED");

        // Escape closes the changelog and leaves About up.
        Key(ui.Window, PhysicalKey.Escape);
        Pump();
        Assert.False(changelog.IsOpen);
        Assert.True(about.IsOpen);
        Key(ui.Window, PhysicalKey.Escape);
        Pump();
        Assert.False(about.IsOpen);
    }

    [AvaloniaFact]
    public async Task TheChangelogIsTheRepositorysAndIsKeptForWhenItCantBeReached()
    {
        const string repository = "# Changelog\n\n## [9.9.9] - 2030-01-01\n\n### Fixed\n\n- Read from the repository.\n";
        Infrastructure.ChangelogSource.Download = _ => Task.FromResult<string?>(repository);
        Explorer ui = OpenExplorer();

        // At once the built-in copy (nothing was read before), then the repository's, which is kept.
        var changelog = new ChangelogDialog();
        Assert.Contains(changelog.Releases, release => release.Version == "0.0.1");
        changelog.Present(ui.Find<MenuRow>());
        await changelog.Updated;
        Pump();
        Assert.Equal(["9.9.9"], changelog.Releases.Select(release => release.Version));
        Assert.Contains(changelog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Read from the repository.");
        Assert.Equal(repository, File.ReadAllText(Infrastructure.ChangelogSource.CachePath));
        changelog.Close();
        Pump();

        // Offline: the copy kept last time, from the start.
        Infrastructure.ChangelogSource.Forget();
        Infrastructure.ChangelogSource.Download = _ => throw new System.Net.Http.HttpRequestException("offline");
        var offline = new ChangelogDialog();
        await offline.Updated;
        Assert.Equal(["9.9.9"], offline.Releases.Select(release => release.Version));

        // Something that isn't a changelog (a proxy's page) is neither shown nor kept.
        Infrastructure.ChangelogSource.Forget();
        Infrastructure.ChangelogSource.Download = _ => Task.FromResult<string?>("<html>Sign in to continue</html>");
        var page = new ChangelogDialog();
        await page.Updated;
        Assert.Equal(["9.9.9"], page.Releases.Select(release => release.Version));
        Assert.Equal(repository, File.ReadAllText(Infrastructure.ChangelogSource.CachePath));

        // A copy older than the one the app was built with gives way to it, as after an update made offline.
        string builtIn = File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md"));
        string old = "## [0.0.1] - 2026-11-02\n\n### Added\n\n- Old.\n";
        Assert.Same(builtIn, Infrastructure.ChangelogSource.Newer(old, builtIn));
        Assert.Same(builtIn, Infrastructure.ChangelogSource.Newer("not a changelog", builtIn));
        Assert.Same(repository, Infrastructure.ChangelogSource.Newer(repository, builtIn));
    }

    /// <summary>The repository this test file is in: the tests run from a build folder elsewhere.</summary>
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(here))!;

    [AvaloniaFact]
    public void TheChangelogScrollsWithTheWheelAnywhereOverIt()
    {
        Explorer ui = OpenExplorer();
        var changelog = new ChangelogDialog();
        changelog.Present(ui.Find<MenuRow>());
        Pump();
        ScrollViewer scroller = changelog.GetVisualDescendants().OfType<ScrollViewer>().Single();
        // Between the text and the scroll bar, where no text is: still part of the list.
        Point gap = scroller.TranslatePoint(new Point(scroller.Bounds.Width - 12 - 4, scroller.Bounds.Height / 2), ui.Window)!.Value;
        Assert.Empty(ui.Window.GetVisualsAt(gap).OfType<TextBlock>());
        ui.Window.MouseWheel(gap, new Vector(0, -1));
        TestWait.Until(() => Math.Abs(scroller.Offset.Y - SmoothScrolling.NotchDistance) < 0.05, "the changelog to glide a notch");
        changelog.Close();
        Pump();
    }

    [AvaloniaFact]
    public void TabNavigationStaysInsideTheOpenModal()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump();
        SettingsDialog settings = menus.Settings!;
        ModalOverlay overlay = settings.FindAncestorOfType<ModalOverlay>()!;
        bool Inside() => ui.Window.FocusManager!.GetFocusedElement() is Visual focused
            && (focused == overlay || overlay.IsVisualAncestorOf(focused));

        // Forwards past the last control and backwards past the first, focus never reaches the explorer.
        var seen = new HashSet<object>();
        for (int i = 0; i < 40; i++)
        {
            KeyNow(ui.Window, PhysicalKey.Tab);
            Assert.True(Inside(), $"Tab {i} left the modal");
            seen.Add(ui.Window.FocusManager!.GetFocusedElement()!);
        }
        for (int i = 0; i < 40; i++)
        {
            KeyNow(ui.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(Inside(), $"Shift+Tab {i} left the modal");
        }
        // And it does move between the sheet's controls rather than sticking on one.
        Assert.True(seen.Count > 3);

        // The title bar stays usable, so focus can land on it: a click on Maximize. Tab still comes back
        // into the modal, and Escape still closes it.
        var maximize = ui.Window.GetVisualDescendants().OfType<Views.Controls.WindowControlButton>().Single(button => button.Name == "MaximizeButton");
        Point onMaximize = maximize.TranslatePoint(new Point(maximize.Bounds.Width / 2, maximize.Bounds.Height / 2), ui.Window)!.Value;
        Click(ui.Window, onMaximize);
        maximize.Focus();
        Assert.False(Inside());
        Key(ui.Window, PhysicalKey.Tab);
        Assert.True(Inside(), "Tab from the title bar did not come back into the modal");
        maximize.Focus();
        Assert.False(Inside());
        Key(ui.Window, PhysicalKey.Escape);
        Pump();
        Assert.False(settings.IsOpen);
        Assert.False(ModalOverlay.IsOpen(ui.Window));
        ui.Window.WindowState = WindowState.Normal;
        Pump();
    }

    [AvaloniaFact]
    public void SettingsEditsTheGamesTheStartupAndTheUpdates()
    {
        string legacy = MakeGame("Legacy");
        HikariHopper.Core.AppSettings.SetStringList(HikariHopper.Core.GameSettings.RecentGameRootsKey, [legacy]);
        Explorer ui = OpenExplorer(openGame: false);
        MenuRow menus = ui.Find<MenuRow>();
        menus.OpenSettings();
        Pump();
        SettingsDialog settings = menus.Settings!;

        // One row per edition, in the catalogue's order, each with its own executable.
        Assert.Equal([("Enhanced", "GTA5_Enhanced.exe"), ("Legacy", "GTA5.exe")],
            settings.Rows.Select(row => (row.Title, row.ExpectedExecutable)));
        GamePathRow legacyRow = settings.Rows[1];
        Assert.Equal("Not set", legacyRow.StatusMessage);

        // A folder chosen is saved and checked, and the folder button offers to change it; the clear mark forgets it.
        Assert.Equal("Choose folder…", legacyRow.BrowseLabel);
        Dialogs.Folders.Enqueue(legacy);
        legacyRow.BrowseButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.Equal(legacy, ui.Tabs.GamePathSettings.LegacyPath);
        Assert.Equal("✓ GTA5.exe found", legacyRow.StatusMessage);
        Assert.Equal("Change folder…", legacyRow.BrowseLabel);
        legacyRow.ClearButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.Equal("", ui.Tabs.GamePathSettings.LegacyPath);
        Assert.Equal("Not set", legacyRow.StatusMessage);

        // Each group in the rail shows its page in place of the others'.
        Assert.Equal(["Games", "Startup", "Updates"], settings.Groups.Select(group => group.Label));
        Assert.Equal("Games", settings.Group);
        Assert.True(settings.GamesPage.IsVisible);
        Assert.False(settings.StartupPage.IsVisible);
        settings.StartupGroup.Choose();
        Pump();
        Assert.Equal("Startup", settings.Group);
        Assert.True(settings.StartupGroup.Selected);
        Assert.False(settings.GamesGroup.Selected);
        Assert.False(settings.GamesPage.IsVisible);
        Assert.True(settings.StartupPage.IsVisible);

        // The keyboard reaches the groups too: Enter or Space chooses the focused one.
        Assert.True(settings.UpdatesGroup.Focusable);
        settings.UpdatesGroup.Focus(NavigationMethod.Tab);
        Key(ui.Window, PhysicalKey.Enter);
        Assert.Equal("Updates", settings.Group);
        settings.GamesGroup.Focus(NavigationMethod.Tab);
        Key(ui.Window, PhysicalKey.Space);
        Assert.Equal("Games", settings.Group);
        settings.StartupGroup.Choose();
        Pump();

        // Reopening the last game is on until it is turned off, and then nothing reopens.
        HikariHopper.Core.AppSettings.SetString(HikariHopper.Core.GameSettings.LastGameRootKey, legacy);
        Assert.True(settings.OpenLastGame.IsOn);
        settings.OpenLastGame.IsOn = false;
        Assert.False(HikariHopper.Core.GameSettings.OpenLastGameOnStartup);
        Assert.Equal("", HikariHopper.Core.GameSettings.GameRootToRestore());
        ui.Bridge.RestoreLastGame();
        Assert.False(ui.Bridge.HasWorkspace);
        settings.OpenLastGame.IsOn = true;
        Assert.Equal(legacy, HikariHopper.Core.GameSettings.GameRootToRestore());

        // The recents the launcher shows can be cleared.
        Assert.Equal("1 game", settings.RecentRow.Detail);
        settings.ClearRecents.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.Empty(ui.Tabs.RecentGames);
        Assert.Equal("None", settings.RecentRow.Detail);
        Assert.False(settings.ClearRecents.IsEnabled);

        // Updates: the version, the check at startup, and Check now.
        settings.UpdatesGroup.Choose();
        Pump();
        Assert.True(settings.UpdatesPage.IsVisible);
        Assert.False(settings.StartupPage.IsVisible);
        Assert.Equal($"HikariHopper {StartPage.AppVersion}", settings.VersionRow.Title);
        Assert.True(settings.CheckNow.IsEnabled);
        Assert.True(settings.CheckForUpdates.IsOn);
        settings.CheckForUpdates.IsOn = false;
        Assert.False(Infrastructure.Updates.CheckOnStartup);
        settings.CheckForUpdates.IsOn = true;
        Assert.True(Infrastructure.Updates.CheckOnStartup);
        settings.Close();
        Pump();

        // Settings reopens on the group it was left at, up to date: the check at startup turned off elsewhere shows off.
        Infrastructure.Updates.CheckOnStartup = false;
        menus.OpenSettings();
        Pump();
        Assert.Equal("Updates", settings.Group);
        Assert.False(settings.CheckForUpdates.IsOn);
        Infrastructure.Updates.CheckOnStartup = true;
        settings.Close();
        Pump();
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
        // Enough folders for both the tree and the list to scroll. The tree highlights the folder you are
        // in, so go into one with enough folders of its own.
        Explorer ui = OpenExplorer(folders: 40);
        for (int index = 0; index < 40; index++)
            Directory.CreateDirectory(Path.Combine(Work, "folder00", $"sub{index:00}"));
        ui.Bridge.Refresh();
        Pump();
        ui.Bridge.Navigate("folder00");
        Pump();
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
        Assert.Equal(0, row.Item!.Depth); // The tree starts inside the root.
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
            // The first double-click opens the folder; the next, the one inside it.
            if (press == 1)
                Assert.Equal("folder00", ui.Bridge.CurrentPath);
        }
        Assert.Equal("folder00/inner", ui.Bridge.CurrentPath);
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
        Pump();
        // The release over the menu that just appeared neither closes it nor triggers an item.
        Assert.True(workspace.EntryContextMenu.IsOpen, "open after release");
        // It is laid out where the press was, in the window's overlay.
        Assert.True(workspace.EntryContextMenu.Bounds.Width >= 230);
        Assert.Equal(Row(3), workspace.EntryContextMenu.TranslatePoint(default, ui.Window));
        Assert.Equal(3, ui.Bridge.SelectedIndex);
        Assert.Equal(".", ui.Bridge.CurrentPath);
    }

    [AvaloniaFact]
    public void TheContextMenuFitsWhatWasRightClicked()
    {
        Write(Path.Combine(Work, "tool.exe"), "MZ");
        Explorer ui = OpenExplorer();
        ExplorerContextMenu menu = ui.Find<WorkspaceView>().EntryContextMenu;
        int Row(string name) => Enumerable.Range(0, ui.Bridge.EntriesModel.Count).Single(row => ui.Bridge.EntriesModel[row].Name == name);
        string[] Shown()
        {
            menu.Refresh();
            return [.. menu.Items.OfType<Views.Controls.RetroMenuItem>().Where(item => item.IsVisible).Select(item => item.IsEnabled ? item.Text : $"({item.Text})")];
        }

        // An item: what you can do with it, and nothing about making something new.
        ui.Bridge.SelectEntry(Row("folder00"));
        Assert.Equal(["Open", "Copy", "Copy name", "Copy path", "Rename…", "Extract…", "Delete", "Properties"], Shown());
        Assert.False(menu.ForFolder);
        ui.Bridge.SelectEntry(Row("file000.txt"));
        Assert.Equal(["Open", "Copy", "Copy name", "Copy path", "Rename…", "Extract…", "Delete", "Properties"], Shown());
        // A file the app can't open: Open is there but off.
        ui.Bridge.SelectEntry(Row("tool.exe"));
        Assert.Equal(["(Open)", "Copy", "Copy name", "Copy path", "Rename…", "Extract…", "Delete", "Properties"], Shown());

        // Several items: no Open, and copying and deleting say how many.
        ui.Bridge.SelectEntry(Row("file000.txt"));
        ui.Bridge.SelectEntry(Row("file001.txt"), KeyModifiers.Control);
        Assert.Equal(["Copy", "Copy names", "Copy paths", "Extract…", "Delete 2 items"], Shown());

        // Empty space: the folder's own menu.
        ui.Bridge.ClearEntrySelection();
        Assert.Equal(["New", "(Paste)", "Refresh"], Shown());
        Assert.True(menu.ForFolder);

        // A right-click on empty space clears the selection and opens that one.
        ui.Bridge.SelectEntry(1);
        Click(ui.Window, new Point(1300, 700), MouseButton.Right);
        Assert.Equal(0, ui.Bridge.SelectionCount);
        Assert.True(menu.IsOpen);
        Assert.True(menu.ForFolder);
        Assert.True(menu.NewEntryMenu.IsVisible);
        Assert.False(menu.DeleteMenuItem.IsVisible);
        menu.Close();
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
        Assert.Equal(Ui.Theme.SheetHeaderHeight + 112 + Ui.Theme.DialogFooterHeight, dialog.Height);
        Assert.Equal("New folder", dialog.Field.SelectedText);

        dialog.Field.Text = "bad:name";
        Pump();
        Assert.NotEqual("", dialog.NameError);
        Assert.Equal(Ui.Theme.SheetHeaderHeight + 112 + 22 + Ui.Theme.DialogFooterHeight, dialog.Height);

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
    public void TheRootIsTheTabNotARowOrACrumb()
    {
        Explorer ui = OpenExplorer();
        NavigationBar navigation = ui.Find<NavigationBar>();
        string game = Path.GetFileName(Work);
        string[] Crumbs() => [.. navigation.Segments.GetVisualDescendants().OfType<NavigationSegment>()
            .Where(segment => segment != navigation.OverflowSegment).Select(segment => segment.Label)];

        // At the root: the tab says Home, the tree starts inside it with nothing focused, and the path bar
        // is empty.
        Assert.Equal("Home", ui.Bridge.TabTitle);
        Assert.DoesNotContain(ui.Bridge.TreeModel, item => item.Label == game);
        Assert.All(ui.Bridge.TreeModel, item => Assert.False(item.Selected));
        Assert.Equal(-1, ui.Bridge.TreeFocusIndex);
        Assert.False(navigation.RootSegment.IsVisible);
        Assert.Empty(Crumbs());

        // In a folder: the tab names it, and the path starts with the home mark, which goes back.
        ui.Bridge.Navigate("folder00");
        Pump();
        Assert.Equal("folder00", ui.Bridge.TabTitle);
        Assert.Equal("folder00", ui.Tabs.Tabs[0].TabTitle);
        Assert.True(navigation.RootSegment.IsVisible);
        Assert.Equal("", navigation.RootSegment.Label);
        Assert.Equal("Home", Avalonia.Automation.AutomationProperties.GetName(navigation.RootSegment));
        Assert.Equal(["folder00"], Crumbs());
        Point home = navigation.RootSegment.TranslatePoint(new Point(navigation.RootSegment.Bounds.Width / 2, 12), ui.Window)!.Value;
        Click(ui.Window, home);
        Assert.Equal(".", ui.Bridge.CurrentPath);
        Assert.Equal("Home", ui.Bridge.TabTitle);
        Assert.False(navigation.RootSegment.IsVisible);
    }

    [AvaloniaFact]
    public void ALongPathFoldsItsLeadingFoldersAndStaysClearOfTheRefreshButton()
    {
        Explorer ui = OpenExplorer(width: 900);
        string[] path = ["x64", "levels", "gta5", "interiors", "dlc_int_01_m23_1", "stream", "props_and_their_textures"];
        Directory.CreateDirectory(Path.Combine([Work, .. path]));
        ui.Bridge.Navigate(string.Join('/', path));
        Pump();
        NavigationBar navigation = ui.Find<NavigationBar>();
        Views.Controls.ChromeToolButton refresh = navigation.AddressBar.GetVisualDescendants()
            .OfType<Views.Controls.ChromeToolButton>().Single();
        double Right(Control control) => control.TranslatePoint(new Point(control.Bounds.Width, 0), navigation)!.Value.X;
        double Left(Control control) => control.TranslatePoint(default, navigation)!.Value.X;

        int hidden = navigation.Segments.HiddenCount;
        Assert.InRange(hidden, 1, path.Length - 1);
        NavigationSegment[] crumbs = [.. navigation.Segments.GetVisualDescendants().OfType<NavigationSegment>()
            .Where(segment => segment != navigation.OverflowSegment)];
        // The current folder shows whole, and everything shown ends before the refresh button.
        NavigationSegment current = crumbs[^1];
        Assert.Equal("props_and_their_textures", current.Label);
        Assert.Equal(current.DesiredSize.Width, current.Bounds.Width, 1);
        Assert.True(Right(current) <= Left(refresh), $"{Right(current)} runs past {Left(refresh)}");
        Assert.True(navigation.OverflowSegment!.Bounds.Width > 0);
        Assert.All(crumbs.Take(hidden), segment => Assert.Equal(0, segment.Bounds.Width));

        // Wider, the whole path fits again: nothing folded, no mark; narrower again, it folds as before.
        ui.Window.Width = 2400;
        Pump();
        Assert.Equal(0, navigation.Segments.HiddenCount);
        Assert.Equal(0, navigation.OverflowSegment!.Bounds.Width);
        Assert.All(crumbs, segment => Assert.True(segment.Bounds.Width > 0));
        ui.Window.Width = 900;
        Pump();
        Assert.Equal(hidden, navigation.Segments.HiddenCount);

        // The "…" mark lists the folded folders, outermost first, and goes to the one picked.
        Point mark = navigation.OverflowSegment.TranslatePoint(new Point(navigation.OverflowSegment.Bounds.Width / 2, 12), ui.Window)!.Value;
        Click(ui.Window, mark);
        Views.Controls.RetroMenu menu = Assert.IsType<Views.Controls.RetroMenu>(navigation.OverflowMenu);
        Assert.Equal(path.Take(hidden), menu.Items.OfType<Views.Controls.RetroMenuItem>().Select(item => item.Text));
        menu.Items.OfType<Views.Controls.RetroMenuItem>().ElementAt(1).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Pump();
        Assert.Equal("x64/levels", ui.Bridge.CurrentPath.Replace('\\', '/'));
    }

    [AvaloniaFact]
    public void ACurrentFolderTooLongForTheBarIsCutShort()
    {
        Explorer ui = OpenExplorer(width: 760);
        string name = "a_folder_whose_name_is_far_too_long_to_fit_in_the_address_bar_at_this_width";
        Directory.CreateDirectory(Path.Combine(Work, "x64", name));
        ui.Bridge.Navigate($"x64/{name}");
        Pump();
        NavigationBar navigation = ui.Find<NavigationBar>();
        Views.Controls.ChromeToolButton refresh = navigation.AddressBar.GetVisualDescendants()
            .OfType<Views.Controls.ChromeToolButton>().Single();
        NavigationSegment current = navigation.Segments.GetVisualDescendants().OfType<NavigationSegment>().Last();
        Assert.Equal(name, current.Label);
        double right = current.TranslatePoint(new Point(current.Bounds.Width, 0), navigation)!.Value.X;
        Assert.True(right <= refresh.TranslatePoint(default, navigation)!.Value.X);
        Assert.True(current.Bounds.Width > 40);
        // Shorter than the whole name would be, and trimmed with an ellipsis.
        var whole = new NavigationSegment();
        whole.Set(name, false, Ui.Theme.Text);
        whole.Measure(Size.Infinity);
        Assert.True(current.Bounds.Width < whole.DesiredSize.Width - 100, $"{current.Bounds.Width} vs {whole.DesiredSize.Width}");
        Assert.Equal(TextTrimming.CharacterEllipsis, current.GetVisualDescendants().OfType<TextBlock>().Single().TextTrimming);
    }

    [AvaloniaFact]
    public void TheHomeMarkInAnRpfOpenedFromOutsideTheGameGoesToTheRpfsRoot()
    {
        Explorer ui = OpenExplorer();
        NavigationBar navigation = ui.Find<NavigationBar>();
        string elsewhere = Path.Combine(Path.GetTempPath(), $"hh-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(elsewhere);
        try
        {
            string external = SaveArchive(Path.Combine(elsewhere, "addon.rpf"), ("stream/readme.txt", "addon"));
            ui.Bridge.OpenArchive(external);
            Pump();
            ui.Bridge.ActivateEntry(ui.Bridge.EntriesModel.Select((item, row) => (item, row)).First(pair => pair.item.Name == "stream").row);
            Pump();
            Assert.Equal("stream", ui.Bridge.TabTitle);
            Assert.Equal("addon.rpf", Avalonia.Automation.AutomationProperties.GetName(navigation.RootSegment));

            // The home mark goes back to the RPF's root: the RPF stays open and the game is not shown.
            Point home = navigation.RootSegment.TranslatePoint(new Point(navigation.RootSegment.Bounds.Width / 2, 12), ui.Window)!.Value;
            Click(ui.Window, home);
            Assert.True(ui.Bridge.InArchive);
            Assert.True(ui.Bridge.ArchiveIsRoot);
            Assert.Equal(".", ui.Bridge.CurrentPath);
            Assert.Equal("addon.rpf", ui.Bridge.TabTitle);
            Assert.False(navigation.RootSegment.IsVisible);
            ui.Bridge.CloseArchive();
            Pump();
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ATextFileOpensInTheTextViewerOverTheExplorer()
    {
        Explorer ui = OpenExplorer();
        const string meta = "<CHandlingDataMgr>\r\n  <Item>\r\n    <fInitialDriveForce value=\"0.3\" />\r\n"
            + "    <fInitialDriveMaxFlatVel value=\"160\" />\r\n  </Item>\r\n</CHandlingDataMgr>";
        Write(Path.Combine(Work, "handling.meta"), meta);
        File.WriteAllBytes(Path.Combine(Work, "binary.meta"), [0x52, 0x53, 0x43, 0x07, 0, 0, 0, 1]);
        ui.Bridge.Refresh();
        Pump();
        int RowOf(string name) => ui.Bridge.EntriesModel.Select((item, row) => (item, row)).First(pair => pair.item.Name == name).row;

        // A file that only has a text file's name is refused.
        ui.Bridge.ActivateEntry(RowOf("binary.meta"));
        Pump();
        Assert.Null(ui.Session.TextViewer);
        Assert.Equal("binary.meta isn't a text file", ui.Bridge.Status);

        ui.Bridge.ActivateEntry(RowOf("handling.meta"));
        Pump();
        TextViewer viewer = ui.Session.TextViewer!;
        Assert.True(viewer.IsOpen);
        Assert.True(ModalOverlay.IsOpen(ui.Window));
        Assert.Equal("handling.meta", viewer.TitleText.Text);
        Assert.Equal(["6 lines", "UTF-8", "CRLF", "XML"], new[] { viewer.LinesText.Text, viewer.EncodingText.Text, viewer.EndingText.Text, viewer.FormatText.Text });
        Assert.Equal("Read only", viewer.StatusText.Text);
        // The app's own quiet scroll bars, both ways: the text can be wider than the view.
        Views.Controls.QuietScrollBar[] bars = [.. viewer.TextScroller.GetVisualDescendants().OfType<Views.Controls.QuietScrollBar>()];
        Assert.Equal([Avalonia.Layout.Orientation.Vertical, Avalonia.Layout.Orientation.Horizontal], bars.Select(bar => bar.Orientation));

        // Ctrl+F, then typing finds; F3 steps through the matches.
        Key(ui.Window, PhysicalKey.F, RawInputModifiers.Control);
        Assert.True(viewer.FindField.IsKeyboardFocusWithin);
        ui.Window.KeyTextInput("initialdrive");
        Pump();
        Assert.Equal(2, viewer.MatchCount);
        Assert.Equal("1 of 2", viewer.MatchText.Text);
        Assert.Equal(new TextPosition(2, 6), viewer.Lines.Selection.Start);
        Key(ui.Window, PhysicalKey.F3);
        Assert.Equal("2 of 2", viewer.MatchText.Text);
        Assert.Equal(3, viewer.Lines.Caret.Line);

        // Escape clears the find first; Ctrl+C copies the selection, or the whole file with none.
        Key(ui.Window, PhysicalKey.Escape);
        Assert.Equal("", viewer.FindField.Text);
        Assert.True(viewer.IsOpen);
        Key(ui.Window, PhysicalKey.C, RawInputModifiers.Control);
        Assert.Equal("InitialDrive", Clipboard.Text);
        viewer.Lines.MoveCaret(new TextPosition(0, 0));
        Key(ui.Window, PhysicalKey.C, RawInputModifiers.Control);
        Assert.Equal(meta, Clipboard.Text);
        Assert.Equal("Copied handling.meta", viewer.StatusText.Text);

        // Then Escape closes it.
        Key(ui.Window, PhysicalKey.Escape);
        Pump();
        Assert.False(viewer.IsOpen);
        Assert.False(ModalOverlay.IsOpen(ui.Window));
        // Closed, it is let go of, and the whole file with it.
        Assert.Null(ui.Session.TextViewer);
    }

    [AvaloniaFact]
    public void ACsvFileOpensAsATableAndCanBeReadAsText()
    {
        Explorer ui = OpenExplorer();
        Write(Path.Combine(Work, "vehicles.csv"), "model,make,price\r\nadder,Truffade,1000000\r\nblista,Dinka,15000\r\n\"two\r\nlines\",x,1\r\n");
        ui.Bridge.Refresh();
        Pump();
        ui.Bridge.ActivateEntry(ui.Bridge.EntriesModel.Select((item, row) => (item, row)).First(pair => pair.item.Name == "vehicles.csv").row);
        Pump();
        TextViewer viewer = ui.Session.TextViewer!;
        Assert.True(viewer.ShowingTable);
        Assert.True(viewer.ViewSwitch.IsVisible);
        Assert.Equal(1, viewer.ViewSwitch.SelectedIndex);
        Assert.Equal("3 rows", viewer.LinesText.Text);
        Assert.Equal("Row 1 of 3", viewer.CaretText.Text);

        // Find marks the cells; the current match's row becomes current.
        viewer.FindField.Text = "dinka";
        Pump();
        Assert.Equal(1, viewer.MatchCount);
        Assert.Equal(2, viewer.Table.CurrentRow);
        Assert.Equal("blista,Dinka,15000", viewer.Copy());

        // The titles are searched too: a match in them leaves the current row where it is.
        viewer.FindField.Text = "make";
        Pump();
        Assert.Equal(1, viewer.MatchCount);
        Assert.Equal(2, viewer.Table.CurrentRow);
        Assert.Equal("1 of 1", viewer.MatchText.Text);
        // A value with a line break shows on one line, and is found as it shows.
        viewer.FindField.Text = "two lines";
        Pump();
        Assert.Equal(1, viewer.MatchCount);
        Assert.Equal(3, viewer.Table.CurrentRow);
        viewer.FindField.Text = "dinka";
        Pump();

        // As text, the same file by its lines.
        viewer.ShowTable(false);
        Assert.False(viewer.ShowingTable);
        Assert.Equal(0, viewer.ViewSwitch.SelectedIndex);
        Assert.Equal("6 lines", viewer.LinesText.Text);
        Assert.Equal(1, viewer.MatchCount);
        viewer.Close();
        Pump();
    }

    [AvaloniaFact]
    public void WindowShortcutsActAndYieldToTheSearchField()
    {
        Explorer ui = OpenExplorer();
        ui.Bridge.Navigate("folder00");
        Pump();
        NavigationBar navigation = ui.Find<NavigationBar>();
        TextBox search = navigation.FindControl<TextBox>("QueryField")!;

        // F3 searches the folder, in the search row.
        Key(ui.Window, PhysicalKey.F3);
        Assert.True(ui.Bridge.IsSearching);
        Assert.True(search.IsFocused);
        ui.Window.KeyTextInput("ab");
        Pump();
        Assert.Equal("ab", ui.Bridge.SearchText);
        // Backspace edits the field instead of going up.
        Key(ui.Window, PhysicalKey.Backspace);
        Assert.Equal("a", ui.Bridge.SearchText);
        Assert.Equal("folder00", ui.Bridge.CurrentPath);
        Assert.Equal("", search.GetValue(Views.Controls.FlatTextField.KeyCapProperty));

        // Escape ends the search and gives the focus up; then Backspace goes up.
        Key(ui.Window, PhysicalKey.Escape);
        Assert.False(ui.Bridge.IsSearching);
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
        // Tree rows: folder00, folder01; the root itself is not one.
        Click(ui.Window, new Point(120, TreeTop + 13));
        Assert.Equal(0, ui.Bridge.TreeFocusIndex);
        Key(ui.Window, PhysicalKey.ArrowDown);
        Assert.Equal(1, ui.Bridge.TreeFocusIndex);
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
        // Hovering the inactive tab raises it a shade lighter than the strip.
        Point hover = tabs[0].TranslatePoint(new Point(tabs[0].Bounds.Width / 2, 16), ui.Window)!.Value;
        ui.Window.MouseMove(hover);
        Pump();
        using (var hovered = ui.Window.CaptureRenderedFrame()!)
            Assert.Equal(HikariHopper.Ui.Theme.TabBg.Color, Pixel(hovered, (int)hover.X, (int)hover.Y - 8));
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
        Pump();
        TabStrip strip = ui.Find<TabStrip>();
        ExplorerViewModel[] before = [.. ui.Tabs.Tabs];
        TabStripTab Drawn(ExplorerViewModel tab) =>
            ui.Window.GetVisualDescendants().OfType<TabStripTab>().Single(candidate => candidate.Tab == tab);
        double Offset(ExplorerViewModel tab) => TabStrip.SlideOffset((Control)Drawn(tab).Parent!);
        Point start = Drawn(before[0]).TranslatePoint(new Point(60, 16), ui.Window)!.Value;
        using IDisposable slow = SlowSlides();

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
        TestWait.Until(() => Math.Abs(Offset(before[1]) + 220) < 0.0005, "the tabs to slide aside");
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
        TestWait.Until(() => Offset(before[0]) == 0, "the dropped tab to settle");
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
        Pump();
        TabStrip strip = ui.Find<TabStrip>();
        ExplorerViewModel[] before = [.. ui.Tabs.Tabs];
        TabStripTab Drawn(ExplorerViewModel tab) =>
            ui.Window.GetVisualDescendants().OfType<TabStripTab>().Single(candidate => candidate.Tab == tab);
        double Offset(ExplorerViewModel tab) => TabStrip.SlideOffset((Control)Drawn(tab).Parent!);
        Control plus = strip.FindControl<Control>("NewTabButton")!;
        double plusLeft = plus.TranslatePoint(default, ui.Window)!.Value.X;
        using IDisposable slow = SlowSlides();

        // Closing the first tab: the others start where they were drawn and glide one tab left.
        TabStripTab first = Drawn(before[0]);
        Click(ui.Window, first.TranslatePoint(new Point(first.Bounds.Width - 14, 16), ui.Window)!.Value);
        Assert.Equal([before[1], before[2]], ui.Tabs.Tabs);
        Pump(20);
        Assert.InRange(Offset(before[1]), 0.1, 219.9);
        Assert.InRange(Offset(before[2]), 0.1, 219.9);
        Assert.InRange(TabStrip.SlideOffset(plus), 0.1, 219.9);
        TestWait.Until(() => Math.Abs(Offset(before[1])) + Math.Abs(Offset(before[2])) + Math.Abs(TabStrip.SlideOffset(plus)) < 0.0005,
            "the tabs to glide into place");
        Assert.Equal(new Point(14, 42), Drawn(before[1]).TranslatePoint(default, ui.Window)!.Value);
        Assert.Equal(plusLeft - 220, plus.TranslatePoint(default, ui.Window)!.Value.X, 3);

        // Closing the last one moves nothing but the button.
        TabStripTab last = Drawn(before[2]);
        Click(ui.Window, last.TranslatePoint(new Point(last.Bounds.Width - 14, 16), ui.Window)!.Value);
        Pump(20);
        Assert.Equal(0, Offset(before[1]), 3);
        Assert.InRange(TabStrip.SlideOffset(plus), 0.1, 219.9);
        TestWait.Until(() => Math.Abs(plus.TranslatePoint(default, ui.Window)!.Value.X - (plusLeft - 440)) < 0.0005, "the new-tab button to glide into place");
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
            .Single(row => row.Item?.Name == name).GetVisualChildren().OfType<TextBlock>().Where(cell => cell.IsVisible).ToArray();

        // Name, type and size only: the count no longer trails the name, and only search results show a location.
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
        Pump();

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
    public void PointingAtAnotherTitleWhileAMenuIsOpenOpensThatMenu()
    {
        Explorer ui = OpenExplorer();
        MenuRow menus = ui.Find<MenuRow>();
        Point CentreOf(Views.Controls.MenuBarButton title) =>
            title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), ui.Window)!.Value;

        // With no menu open, pointing at a title only lights it.
        ui.Window.MouseMove(CentreOf(menus.EditButton));
        Pump(20);
        Assert.False(menus.MenuFor(menus.EditButton).IsOpen);

        Click(ui.Window, CentreOf(menus.FileButton));
        Assert.True(menus.MenuFor(menus.FileButton).IsOpen);
        // Pointing at another title swaps the open menu for that one, one at a time.
        foreach (Views.Controls.MenuBarButton title in new[] { menus.EditButton, menus.ViewButton, menus.GoButton, menus.HelpButton, menus.FileButton })
        {
            ui.Window.MouseMove(CentreOf(title));
            Pump(20);
            Assert.True(menus.MenuFor(title).IsOpen);
            Assert.True(title.MenuOpen);
            Assert.Single(new[] { menus.FileButton, menus.EditButton, menus.ViewButton, menus.GoButton, menus.HelpButton }, other => menus.MenuFor(other).IsOpen);
        }
        menus.MenuFor(menus.FileButton).Close();
        Pump(20);
        Assert.False(menus.FileButton.MenuOpen);
    }

    [AvaloniaFact]
    public void StatusBarShowsTheGameItsItemsAndWhatIsSelected()
    {
        Explorer ui = OpenExplorer();
        StatusBar status = ui.Find<StatusBar>();
        Assert.Equal("GTA V Legacy", ui.Bridge.GameEdition);
        // The game in a well of its own at the start, with its edition's mark, as on its tab.
        Assert.True(status.Edition.IsVisible);
        Assert.Equal("GTA V Legacy", status.EditionText.Text);
        Assert.False(status.EditionMark.Enhanced);
        Assert.Equal(HikariHopper.Ui.Theme.InsetBg, status.Edition.Background);
        // The load confirmation only repeats the game's well.
        Assert.Equal("Loaded GTA V Legacy", ui.Bridge.Status);
        Assert.Equal("", status.VisibleStatus);
        Assert.False(StatusBar.StatusWorthShowing("Loaded GTA V Legacy", "GTA V Legacy"));
        Assert.True(StatusBar.StatusWorthShowing("Dragging one item as a copy", "GTA V Legacy"));
        Assert.True(StatusBar.StatusWorthShowing("Opened sample.rpf", ""));
        Assert.False(StatusBar.StatusWorthShowing("", ""));

        string[] texts = status.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
        Assert.DoesNotContain(texts, text => text.Contains("FiveFury") || text.Contains("Ctrl+O") || text.Contains("F3"));
        // No key hints, and no lamp.
        Assert.Empty(status.GetVisualDescendants().OfType<Views.Controls.KeyCap>());
        Assert.Empty(status.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());

        // What is selected, on the right: how many and how big.
        Assert.Equal("", status.SelectionText.Text);
        ui.Bridge.SelectEntry(0);
        ui.Bridge.SelectEntry(1, KeyModifiers.Control);
        Pump();
        Assert.Equal($"2 selected · {ui.Bridge.SelectedSize}", status.SelectionText.Text);
        Assert.Equal("1 selected", StatusBar.Selection(1, ""));
        ui.Bridge.ClearEntrySelection();
        Pump();
        Assert.Equal("", status.SelectionText.Text);

        // A standalone archive has no edition, so no well.
        ui.Bridge.CloseArchive();
        ui.Tabs.NewTab();
        Pump();
        Assert.Equal("", ui.Tabs.ActiveBridge.GameEdition);
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
        TestWait.Until(() => Math.Abs(scroller.Offset.Y - SmoothScrolling.NotchDistance) < 0.0005, "the glide to cover a notch");
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
        TestWait.Until(() => Math.Abs(scroller.Offset.Y - 3 * SmoothScrolling.NotchDistance) < 0.0005, "the glides to add up");
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
        TestWait.Until(() => rows.RenderTransform is null, "the list to spring back");

        // The bottom stretches the other way.
        scroller.Offset = scroller.Offset.WithY(scroller.Extent.Height - scroller.Viewport.Height);
        Pump();
        ui.Window.MouseWheel(new Point(600, 400), new Vector(0, -1));
        Pump(40);
        Assert.True(rows.RenderTransform is Avalonia.Media.TranslateTransform { Y: < 0 }, "The bottom of the list should stretch up");
        TestWait.Until(() => rows.RenderTransform is null, "the list to spring back");
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
        Pump(150);
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
