using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The menu bar behaviour of every desktop app: while one of the bar's menus is open, pointing at another
/// title opens that menu in its place.
/// </summary>
/// <remarks>
/// An open menu lays a light-dismiss overlay over the window, which takes the pointer, so the titles never
/// see it arrive. The pointer is watched instead where it enters the window, on the tunnel route, before
/// the overlay or anything else handles it.
/// </remarks>
internal sealed class MenuBarHover
{
    private readonly IReadOnlyList<(MenuBarButton Title, RetroMenu Menu, Action Open)> titles;
    private TopLevel? top;

    private MenuBarHover(Control bar, IReadOnlyList<(MenuBarButton, RetroMenu, Action)> titles)
    {
        this.titles = titles;
        bar.AttachedToVisualTree += (_, _) => Watch(TopLevel.GetTopLevel(bar));
        bar.DetachedFromVisualTree += (_, _) => Watch(null);
        Watch(TopLevel.GetTopLevel(bar));
    }

    /// <summary>
    /// Makes <paramref name="bar"/>'s titles switch menus on hover; <c>Open</c> opens a title's menu as a click
    /// on it does.
    /// </summary>
    public static void Attach(Control bar, params (MenuBarButton Title, RetroMenu Menu, Action Open)[] titles) =>
        _ = new MenuBarHover(bar, titles);

    private void Watch(TopLevel? next)
    {
        if (ReferenceEquals(next, top))
            return;
        top?.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        top = next;
        top?.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        RetroMenu? open = titles.Select(entry => entry.Menu).FirstOrDefault(menu => menu.IsOpen);
        if (open is null)
            return;
        foreach ((MenuBarButton title, RetroMenu menu, Action openMenu) in titles)
        {
            if (ReferenceEquals(menu, open) || !title.IsEffectivelyVisible || !title.IsEffectivelyEnabled)
                continue;
            if (!new Rect(title.Bounds.Size).Contains(e.GetPosition(title)))
                continue;
            open.Close();
            openMenu();
            return;
        }
    }
}
