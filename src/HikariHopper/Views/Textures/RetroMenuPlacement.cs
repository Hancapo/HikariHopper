using Avalonia;
using Avalonia.Controls;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Opens a <see cref="RetroMenu"/> the way the QML window positions a <c>Menu</c> given <c>x</c>/<c>y</c>
/// and <c>open()</c>: the menu's <c>margins: 0</c> keep it inside the window, so it slides left or up
/// rather than past the window edge.
/// </summary>
internal static class RetroMenuPlacement
{
    private const double MenuVerticalPadding = 8;

    /// <summary>Opens <paramref name="menu"/> at <paramref name="point"/> in <paramref name="relativeTo"/>'s coordinates.</summary>
    public static void Open(RetroMenu menu, Visual relativeTo, Point point)
    {
        if (TopLevel.GetTopLevel(relativeTo) is not { } window || relativeTo.TranslatePoint(point, window) is not { } at)
            return;
        Size size = MenuSize(menu);
        double x = Math.Max(0, Math.Min(at.X, window.Bounds.Width - size.Width));
        double y = Math.Max(0, Math.Min(at.Y, window.Bounds.Height - size.Height));
        menu.Open(window, x, y);
    }

    /// <summary>The menu's size before it is shown: its fixed width and the sum of its rows plus padding.</summary>
    public static Size MenuSize(RetroMenu menu)
    {
        double height = MenuVerticalPadding;
        foreach (object? item in menu.Items)
        {
            if (item is Control { IsVisible: false })
                continue;
            height += item switch
            {
                RetroMenuSeparator => Ui.Theme.MenuSeparatorHeight,
                RetroMenuSection => 22,
                Control control when !double.IsNaN(control.Height) => control.Height,
                _ => Ui.Theme.MenuItemHeight,
            };
        }
        double width = double.IsNaN(menu.Width) ? Ui.Theme.MenuMinimumWidth + 2 : menu.Width;
        return new Size(width, height);
    }
}
