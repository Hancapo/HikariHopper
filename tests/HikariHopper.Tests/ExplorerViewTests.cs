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
    // Title bar 30 + menu 26 + tabs 28 + navigation 40 + header 24.
    private const double FirstRowTop = 148;
    private const double TableLeft = 248;

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
    public void TableColumnsFollowThePanelWidth()
    {
        Explorer ui = OpenExplorer();
        EntryTable table = ui.Find<EntryTable>();
        double width = table.Bounds.Width;

        Assert.Equal(Math.Max(300, Math.Min(500, Math.Floor(width * 0.46 + 0.5))), table.NameWidth);
        Assert.Equal(table.NameWidth + 190 + 124, table.DataBlockWidth);
        EntryRow row = table.GetVisualDescendants().OfType<EntryRow>().First();
        Assert.Equal(26, row.Bounds.Height);
        Assert.Equal(Math.Min(table.DataBlockWidth, table.Bounds.Width - 12), row.Bounds.Width);
        Assert.Equal(247, ui.Find<FolderPanel>().Bounds.Width);
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
        Explorer ui = OpenExplorer(files: 10);
        EntryTable table = ui.Find<EntryTable>();
        ui.Bridge.SelectEntry(0);
        double background = TableLeft + table.DataBlockWidth + 20;

        Click(ui.Window, new Point(background, Row(1).Y));
        Assert.Equal(0, ui.Bridge.SelectionCount);

        // A drag that starts beside the rows selects the rows the band crosses.
        ui.Window.MouseMove(new Point(background, Row(2).Y));
        ui.Window.MouseDown(new Point(background, Row(2).Y), MouseButton.Left);
        ui.Window.MouseMove(new Point(background - 3, Row(2).Y + 3), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.False(table.MarqueeActive);
        ui.Window.MouseMove(new Point(500, Row(5).Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.True(table.MarqueeActive);
        Assert.True(table.MarqueeVisual.IsVisible);
        Assert.Equal(4, ui.Bridge.SelectionCount);
        ui.Window.MouseUp(new Point(500, Row(5).Y), MouseButton.Left);
        Pump();
        Assert.False(table.MarqueeVisual.IsVisible);
        Assert.Equal([2, 3, 4, 5], ui.Bridge.EntriesModel.Select((item, index) => (item, index)).Where(pair => pair.item.Selected).Select(pair => pair.index));
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
        Point size = new(TableLeft + table.NameWidth + 190 + 60, 136);
        Click(ui.Window, size);
        Assert.Equal("size", ui.Bridge.SortColumn);
        Assert.True(ui.Bridge.SortAscending);
        Click(ui.Window, size);
        Assert.False(ui.Bridge.SortAscending);
        Assert.Equal("file000.txt", ui.Bridge.EntriesModel[^1].Name);
        Click(ui.Window, new Point(TableLeft + 40, 136));
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
        Click(ui.Window, new Point(120, FirstRowTop - 24 + 26 + 13));
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
        Click(ui.Window, new Point(TableLeft + 10 + 74, FirstRowTop + 56));
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
        ui.Window.MouseMove(new Point(right, FirstRowTop + 40));
        ui.Window.MouseDown(new Point(right, FirstRowTop + 40), MouseButton.Left);
        ui.Window.MouseMove(new Point(TableLeft + 10 + 148 + 160, FirstRowTop + 120), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.True(grid.MarqueeActive);
        var expected = GridMath.IndicesInRect(ui.Bridge.EntriesModel.Count, grid.ColumnsVisible, 148, 112, 10, 10,
            TableLeft + 10 + 148 + 160 - TableLeft - 10, right - TableLeft - 10, 40, 120);
        Assert.Equal(expected.Count, ui.Bridge.SelectionCount);
        ui.Window.MouseUp(new Point(TableLeft + 300, FirstRowTop + 120), MouseButton.Left);
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
        Assert.Equal(150, tabs[1].Bounds.Width);
        Click(ui.Window, tabs[0].TranslatePoint(new Point(30, 12), ui.Window)!.Value);
        Assert.Equal(0, ui.Tabs.ActiveIndex);
        Click(ui.Window, tabs[1].TranslatePoint(new Point(tabs[1].Bounds.Width - 14, 12), ui.Window)!.Value);
        Assert.Single(ui.Tabs.Tabs);
        Assert.Single(ui.Session.TextureWindows);
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
    public void WheelScrollsThreeLinesPerNotch()
    {
        Explorer ui = OpenExplorer(files: 80);
        ScrollViewer scroller = ui.Find<EntryTable>().FindControl<ScrollViewer>("Scroller")!;
        ui.Window.MouseMove(new Point(600, 400));
        ui.Window.MouseWheel(new Point(600, 400), new Vector(0, -1));
        Pump(30);
        Assert.InRange(scroller.Offset.Y, 1, 71);
        Pump(400);
        Assert.Equal(QtWheelScrolling.NotchDistance, scroller.Offset.Y, 3);
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
