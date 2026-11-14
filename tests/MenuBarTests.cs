using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HikariHopper.Views;
using HikariHopper.Views.Controls;

namespace HikariHopper.Tests;

/// <summary>Every menu bar answers the pointer and the keys alike: the explorer's, the texture editor's and the viewers'.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class MenuBarTests : ExplorerTestBase
{
    private static Point Centre(Control control, Visual to) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), to)!.Value;

    private static void Click(Window window, Control control)
    {
        Point at = Centre(control, window);
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        TestWait.Pump(60);
    }

    private static void Press(TopLevel window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        // A key that closed its menu's window has no window to let go in.
        if (window is not Window { IsVisible: false })
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
        TestWait.Pump(40);
    }

    private static string Lit(IEnumerable<RetroMenuItem> items) =>
        string.Join(", ", items.Where(item => item.Highlighted).Select(item => item.Text));

    [AvaloniaFact]
    public void The_viewers_menu_bar_switches_menus_under_the_pointer_and_answers_the_keys()
    {
        var runs = new List<string>();
        ViewerMenuRow.Item Item(string text, bool enabled = true) => new(text, "", () => runs.Add(text), () => enabled);
        var row = new ViewerMenuRow(("File", [Item("Save", enabled: false), Item("Save as…"), null, Item("Close")]),
            ("Edit", [Item("Undo"), Item("Redo")]), ("View", [Item("Grid")]));
        var window = new Window { Width = 600, Height = 300, Content = new DockPanel { LastChildFill = false, Children = { row } } };
        DockPanel.SetDock(row, Dock.Top);
        window.Show();
        TestWait.Pump(100);
        MenuBarButton Title(string text) => row.GetVisualDescendants().OfType<MenuBarButton>().Single(title => title.Text == text);
        List<RetroMenuItem> Items() => [.. row.OpenWindow!.GetVisualDescendants().OfType<RetroMenuItem>()];
        try
        {
            // While one is open, pointing at another title opens that one in its place.
            Click(window, Title("File"));
            Assert.Equal("File", row.OpenTitle);
            window.MouseMove(Centre(Title("Edit"), window));
            TestWait.Pump(60);
            Assert.Equal("Edit", row.OpenTitle);
            window.MouseMove(Centre(Title("View"), window));
            TestWait.Pump(60);
            Assert.Equal("View", row.OpenTitle);

            // Right from the last goes round to the first, its first item that can be used lit; Down skips the
            // separator and goes round too; Enter runs the item lit and closes the menu.
            Press(row.OpenWindow!, Key.Right, PhysicalKey.ArrowRight);
            Assert.Equal("File", row.OpenTitle);
            Assert.Equal("Save as…", Lit(Items()));
            Press(row.OpenWindow!, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal("Close", Lit(Items()));
            Press(row.OpenWindow!, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal("Save as…", Lit(Items()));
            Press(row.OpenWindow!, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal("View", row.OpenTitle);
            Press(row.OpenWindow!, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal("Edit", row.OpenTitle);
            Press(row.OpenWindow!, Key.Up, PhysicalKey.ArrowUp);
            Assert.Equal("Redo", Lit(Items()));
            Press(row.OpenWindow!, Key.Up, PhysicalKey.ArrowUp);
            Assert.Equal("Undo", Lit(Items()));
            Press(row.OpenWindow!, Key.Enter, PhysicalKey.Enter);
            Assert.Null(row.OpenTitle);
            Assert.Equal(["Undo"], runs);

            // A click on the open menu's title closes it; the next click opens it again, however soon.
            Click(window, Title("Edit"));
            Assert.Equal("Edit", row.OpenTitle);
            Click(window, Title("Edit"));
            Assert.Null(row.OpenTitle);
            Click(window, Title("Edit"));
            Assert.Equal("Edit", row.OpenTitle);
            Press(row.OpenWindow!, Key.Escape, PhysicalKey.Escape);
            Assert.Null(row.OpenTitle);
            Click(window, Title("Edit"));
            Assert.Equal("Edit", row.OpenTitle);
        }
        finally
        {
            row.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_explorers_menu_bar_closes_on_its_title_and_escape_and_answers_the_keys()
    {
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1200, Height = 700 };
        session.Attach(window);
        window.Show();
        TestWait.Pump(200);
        MenuRow row = window.GetVisualDescendants().OfType<MenuRow>().Single();
        MenuBarButton Title(string text) => row.GetVisualDescendants().OfType<MenuBarButton>().Single(title => title.Text == text);
        RetroMenu file = row.MenuFor(Title("File")), edit = row.MenuFor(Title("Edit"));
        int opened = 0;
        file.Opened += (_, _) => opened++;
        try
        {
            // A click on the open menu's title closes it, rather than closing and opening it again.
            Click(window, Title("File"));
            Assert.True(file.IsOpen);
            Click(window, Title("File"));
            Assert.False(file.IsOpen);
            Assert.Equal(1, opened);
            Click(window, Title("File"));
            Assert.True(file.IsOpen);

            // Escape closes it, on the start page too, where there's no workspace to see the key.
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(file.IsOpen);

            // Opened with the pointer, the keys move through it and to the menus beside it.
            Click(window, Title("File"));
            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal("New tab", Lit(file.Items.OfType<RetroMenuItem>()));
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.True(edit.IsOpen);
            Assert.False(file.IsOpen);
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.True(file.IsOpen);
            Assert.Equal("New tab", Lit(file.Items.OfType<RetroMenuItem>()));
            // Enter runs it: a new tab, the menu closed, and the keyboard back with the window.
            int tabs = session.Tabs.Tabs.Count;
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.False(file.IsOpen);
            Assert.Equal(tabs + 1, session.Tabs.Tabs.Count);
            Assert.False(file.IsKeyboardFocusWithin);
            // And the window's keys still reach the bar: the next menu opens with a click and closes with Escape.
            Click(window, Title("File"));
            Press(window, Key.Escape, PhysicalKey.Escape);
            Assert.False(file.IsOpen);
        }
        finally
        {
            file.Close();
            edit.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Enter_and_space_toggle_a_checkable_item_as_a_click_does()
    {
        MakeGame();
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1200, Height = 700 };
        session.Attach(window);
        window.Show();
        session.Tabs.ActiveBridge.OpenGame(Work);
        TestWait.Pump(200);
        MenuRow row = window.GetVisualDescendants().OfType<MenuRow>().Single();
        MenuBarButton title = row.GetVisualDescendants().OfType<MenuBarButton>().Single(title => title.Text == "View");
        RetroMenu view = row.MenuFor(title);
        bool shown = session.Tabs.ActiveBridge.FoldersVisible;
        void Light()
        {
            Click(window, title);
            for (int step = 0; step < 8 && Lit(view.Items.OfType<RetroMenuItem>()) != "Folders pane"; step++)
                Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.Equal("Folders pane", Lit(view.Items.OfType<RetroMenuItem>()));
        }
        try
        {
            // Enter turns the folders pane off, as a click on it does, and Space turns it on again.
            Light();
            Press(window, Key.Enter, PhysicalKey.Enter);
            Assert.False(view.IsOpen);
            Assert.Equal(!shown, session.Tabs.ActiveBridge.FoldersVisible);
            Light();
            Press(window, Key.Space, PhysicalKey.Space);
            Assert.Equal(shown, session.Tabs.ActiveBridge.FoldersVisible);
        }
        finally
        {
            view.Close();
            window.Close();
        }
    }
}
