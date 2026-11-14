using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A viewer's menu bar, as the texture editor's: titles (File, Edit) on the chrome under the viewer's title, each
/// opening its menu hanging from it. The menus open as windows of their own (<see cref="DropdownWindow.OpenMenu"/>), so
/// they are drawn over a native child window such as the 3D viewport. Each item says when it can be used, asked as its
/// menu opens; the keys shown are the viewer's own, handled by the viewer. Its clicks, pointing and keys are a menu bar's,
/// as the explorer's and the texture editor's (<see cref="MenuBar"/>).
/// </summary>
internal sealed class ViewerMenuRow : Panel
{
    /// <summary>A menu's item: what it says, its key, what it does, and when it can be used (always, when none).</summary>
    internal sealed record Item(string Text, string Shortcut, Action Run, Func<bool>? Enabled = null);

    private readonly List<(MenuBarButton Title, RetroMenu Menu, Dictionary<RetroMenuItem, Item> Items)> menus = [];
    private readonly MenuBar bar;
    private DropdownWindow? open;
    private RetroMenu? openMenu;

    /// <param name="titles">Each menu's title and items, in order; a null item is a separator.</param>
    public ViewerMenuRow(params (string Title, Item?[] Items)[] titles)
    {
        Height = Ui.Theme.MenuHeight;
        Background = Ui.Theme.ChromeBg;
        Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = Ui.Theme.BorderHard });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 0, 0, 0) };
        Children.Add(row);
        foreach ((string title, Item?[] items) in titles)
        {
            var button = new MenuBarButton { Text = title, Padding = new Thickness(10, 0), FontSize = 13 };
            var menu = new RetroMenu();
            var known = new Dictionary<RetroMenuItem, Item>();
            foreach (Item? item in items)
            {
                if (item is null)
                {
                    menu.Items.Add(new RetroMenuSeparator());
                    continue;
                }
                var entry = new RetroMenuItem { Text = item.Text, ShortcutText = item.Shortcut };
                entry.Triggered += (_, _) =>
                {
                    Close();
                    if (item.Enabled?.Invoke() != false)
                        item.Run();
                };
                known[entry] = item;
                menu.Items.Add(entry);
            }
            menus.Add((button, menu, known));
            row.Children.Add(button);
        }
        bar = MenuBar.Attach(this, [.. menus.Select(entry => new MenuBar.Title(entry.Title, entry.Menu, () => openMenu == entry.Menu,
            () => Open(entry.Menu), Close))]);
        foreach (var (title, _, _) in menus)
            title.Click += (_, _) => bar.Toggle(title);
    }

    /// <summary>The menus' titles, in order.</summary>
    internal IReadOnlyList<string> Titles => [.. menus.Select(menu => menu.Title.Text)];

    /// <summary>A menu's items, by what they say, and whether each can be used now; a separator is "-".</summary>
    internal IReadOnlyList<(string Text, string Shortcut, bool Enabled)> ItemsOf(string title)
    {
        var (_, menu, items) = menus.Single(entry => entry.Title.Text == title);
        Refresh(menu, items);
        return [.. menu.Items.Select(item => item is RetroMenuItem entry ? (entry.Text, entry.ShortcutText, entry.IsEnabled) : ("-", "", false))];
    }

    /// <summary>Runs an item, as a click on it does, when it can be used; false when it can't.</summary>
    internal bool Run(string title, string text)
    {
        var (_, menu, items) = menus.Single(entry => entry.Title.Text == title);
        Item item = items.Single(entry => entry.Key.Text == text).Value;
        if (item.Enabled?.Invoke() == false)
            return false;
        item.Run();
        return true;
    }

    /// <summary>The window of the menu open; none when none is.</summary>
    internal DropdownWindow? OpenWindow => open;

    /// <summary>The menu open, by its title; none when none is.</summary>
    internal string? OpenTitle => openMenu is null ? null : menus.Single(entry => entry.Menu == openMenu).Title.Text;

    /// <summary>Opens a menu, or closes it when it's the one open, as a click on its title does.</summary>
    internal void Toggle(string title) => bar.Toggle(menus.Single(entry => entry.Title.Text == title).Title);

    private void Open(RetroMenu menu)
    {
        Close();
        var (title, _, items) = menus.Single(entry => entry.Menu == menu);
        Refresh(menu, items);
        open = DropdownWindow.OpenMenu(title, menu);
        if (open is null)
            return;
        openMenu = menu;
        title.MenuOpen = true;
        DropdownWindow shown = open;
        // Its window takes the keyboard: its keys go to the bar first, as the window's do for an in-window menu.
        shown.AddHandler(KeyDownEvent, bar.OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        shown.Closed += (_, _) =>
        {
            if (open != shown)
                return;
            title.MenuOpen = false;
            open = null;
            openMenu = null;
            // Taken from it, by a click on its title among others: that click doesn't open it again.
            bar.Closed(title);
        };
    }

    /// <summary>Closes the menu open, if one is.</summary>
    public void Close() => open?.Close();

    // Each item can be used or not as things are now.
    private static void Refresh(RetroMenu menu, Dictionary<RetroMenuItem, Item> items)
    {
        foreach ((RetroMenuItem entry, Item item) in items)
            entry.IsEnabled = item.Enabled?.Invoke() != false;
    }
}
