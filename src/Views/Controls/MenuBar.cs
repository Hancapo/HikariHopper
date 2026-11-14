using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The menu bar behaviour of every desktop app, the same for the explorer's, the texture editor's and the viewers' bars:
/// a click on a title opens its menu and a click on it again closes it; while a menu is open, pointing at another title
/// opens that menu in its place; Up and Down move through its items, Enter or Space runs the one lit, Left and Right
/// open the menus beside it, and Escape closes it.
/// </summary>
/// <remarks>
/// A menu opened with the pointer leaves the keyboard with the window, as a light-dismiss overlay takes the pointer: the
/// titles never see the pointer arrive, nor the menu the keys. Both are watched instead where they enter the window, on
/// the tunnel route, before anything else handles them; and through the menu too, as the item the keys light takes the
/// keyboard. A menu that opens as a window of its own (the viewers', over the 3D viewport) takes the keyboard itself;
/// its bar hands its keys here (<see cref="OnKeyDown"/>).
/// </remarks>
internal sealed class MenuBar
{
    /// <summary>
    /// A title and its menu: whether the menu is open, how to open and close it, and the menu itself, whose items the keys
    /// move through.
    /// </summary>
    internal sealed record Title(MenuBarButton Button, RetroMenu Menu, Func<bool> IsOpen, Action Open, Action Close);

    // How long after a menu closes a click on its title still counts as the press that closed it: a menu that closes
    // when the pointer goes down outside it does so before its title sees the click.
    private const long ClosingClick = 400;

    private readonly IReadOnlyList<Title> titles;
    private readonly Dictionary<Title, long> closedAt = [];
    private TopLevel? top;
    // While the bar closes a menu itself, which no click on its title follows.
    private bool closing;

    private MenuBar(Control bar, IReadOnlyList<Title> titles)
    {
        this.titles = titles;
        // Once the keys light an item, it has the keyboard, in the menu's own popup: its keys come through the menu.
        foreach (Title title in titles)
            title.Menu.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        bar.AttachedToVisualTree += (_, _) => Watch(TopLevel.GetTopLevel(bar));
        bar.DetachedFromVisualTree += (_, _) => Watch(null);
        Watch(TopLevel.GetTopLevel(bar));
    }

    /// <summary>Gives <paramref name="bar"/>'s titles the menu bar's behaviour; the bar says when each menu closes (<see cref="Closed"/>).</summary>
    public static MenuBar Attach(Control bar, params Title[] titles) => new(bar, titles);

    /// <summary>The click on a title: opens its menu, or closes it when it's open, or was just closed by the same press.</summary>
    public void Toggle(MenuBarButton button)
    {
        Title title = titles.Single(entry => entry.Button == button);
        if (title.IsOpen())
        {
            Close(title);
            return;
        }
        // The click of the press that closed it, once: the next click opens it again, however soon.
        if (closedAt.Remove(title, out long at) && Environment.TickCount64 - at < ClosingClick)
            return;
        Open(title, light: false);
    }

    /// <summary>
    /// The bar's word that a title's menu closed, however it did. One it didn't close itself went with a press outside it,
    /// which may be on its title, whose click then mustn't open it again.
    /// </summary>
    public void Closed(MenuBarButton button)
    {
        if (!closing)
            closedAt[titles.Single(entry => entry.Button == button)] = Environment.TickCount64;
    }

    private void Close(Title title)
    {
        closing = true;
        try
        {
            title.Close();
        }
        finally
        {
            closing = false;
        }
    }

    private Title? OpenTitle => titles.FirstOrDefault(title => title.IsOpen());

    // Opens a title's menu in place of any other, its first item lit when the keyboard opened it.
    private void Open(Title title, bool light)
    {
        if (OpenTitle is { } open)
            Close(open);
        title.Open();
        if (light && title.IsOpen())
            Move(title, 1);
    }

    private void Watch(TopLevel? next)
    {
        if (ReferenceEquals(next, top))
            return;
        top?.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        top?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        top = next;
        top?.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static bool Usable(Title title) => title.Button.IsEffectivelyVisible && title.Button.IsEffectivelyEnabled;

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (OpenTitle is not { } open)
            return;
        foreach (Title title in titles)
        {
            if (title == open || !Usable(title) || !new Rect(title.Button.Bounds.Size).Contains(e.GetPosition(title.Button)))
                continue;
            Open(title, light: false);
            return;
        }
    }

    /// <summary>A key while one of the bar's menus is open, from its window or the menu's own.</summary>
    public void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || OpenTitle is not { } open)
            return;
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                Step(open, e.Key == Key.Right ? 1 : -1);
                break;
            case Key.Up or Key.Down:
                Move(open, e.Key == Key.Down ? 1 : -1);
                break;
            case Key.Enter or Key.Space:
                // Closed first, as a click on it does, so what it opens comes up over the window, not the menu.
                if (Lit(open) is { } item)
                {
                    Close(open);
                    // Checked or cleared first, as Avalonia's own menus do before the click, so the item sees itself
                    // toggled.
                    if (item.ToggleType == MenuItemToggleType.CheckBox)
                        item.IsChecked = !item.IsChecked;
                    else if (item.ToggleType == MenuItemToggleType.Radio)
                        item.IsChecked = true;
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                break;
            case Key.Escape:
                Close(open);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // The menu beside the open one, round from the last to the first, its first item lit.
    private void Step(Title open, int by)
    {
        int count = titles.Count, at = IndexOf(open);
        for (int step = 1; step < count; step++)
        {
            Title next = titles[((at + by * step) % count + count) % count];
            if (Usable(next))
            {
                Open(next, light: true);
                return;
            }
        }
    }

    private int IndexOf(Title title)
    {
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] == title)
                return i;
        return -1;
    }

    // The items the keys move through: those shown that can be used, not the separators.
    private static List<RetroMenuItem> Items(Title title) =>
        [.. title.Menu.Items.OfType<RetroMenuItem>().Where(item => item.IsVisible && item.IsEffectivelyEnabled)];

    // The item lit, by the pointer or the keys.
    private static RetroMenuItem? Lit(Title title) => Items(title).FirstOrDefault(item => item.IsSelected);

    // Lights the next item that can be used, round from the last to the first; the first or last when none is lit.
    private static void Move(Title title, int by)
    {
        List<RetroMenuItem> items = Items(title);
        if (items.Count == 0)
            return;
        int at = Lit(title) is { } lit ? items.IndexOf(lit) : -1;
        int next = at < 0 ? (by > 0 ? 0 : items.Count - 1) : ((at + by) % items.Count + items.Count) % items.Count;
        // Only that one lit, whatever the pointer lit before.
        foreach (RetroMenuItem item in title.Menu.Items.OfType<RetroMenuItem>())
            item.IsSelected = item == items[next];
    }
}
