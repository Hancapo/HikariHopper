using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A dropdown in a menu's frame that opens under a control as a window of its own, borderless and owned by the main
/// window. The app's popups are drawn inside their window, under any native child window there (the 3D viewport); this
/// one is drawn over it. It closes when another window takes the keyboard, a click lands outside it, or Escape.
/// </summary>
internal sealed class DropdownWindow : Window
{
    private readonly Control body;

    /// <param name="frame">Whether it draws a menu's frame round <paramref name="body"/>; a menu draws its own.</param>
    private DropdownWindow(Control body, bool frame = true)
    {
        this.body = body;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        // The frame and its shadow, with room for the shadow around it, as a menu's.
        Content = frame
            ? new Panel { Margin = Ui.Theme.MenuShadowMargin, Children = { new RetroMenuFrame { ConnectionWidth = 0 }, body } }
            : new Panel { Children = { body } };
        Deactivated += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsVisible) Close(); });
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Closed += (_, _) => ((Panel)Content!).Children.Remove(body);
    }

    /// <summary>
    /// Opens a menu hanging from a menu bar's title, as the texture editor's do: its frame's open top edge one pixel up
    /// into the title, which draws the rest of the outline; none when the title isn't in a window. A window of its own,
    /// so it is drawn over the 3D viewport.
    /// </summary>
    public static DropdownWindow? OpenMenu(Control title, RetroMenu menu)
    {
        if (TopLevel.GetTopLevel(title) is not Window owner)
            return null;
        menu.ConnectionWidth = title.Bounds.Width;
        var dropdown = new DropdownWindow(menu, frame: false);
        Thickness margin = menu.Margin;
        double scale = owner.RenderScaling;
        PixelPoint corner = title.PointToScreen(new Point(0, title.Bounds.Height - 1));
        dropdown.Position = new PixelPoint((int)Math.Round(corner.X - margin.Left * scale), (int)Math.Round(corner.Y - margin.Top * scale));
        dropdown.Show(owner);
        return dropdown;
    }

    /// <summary>
    /// Opens <paramref name="body"/> as a context menu: its corner at <paramref name="at"/> in <paramref name="anchor"/>,
    /// moved back onto the screen where it would run off it; none when the anchor isn't in a window.
    /// </summary>
    public static DropdownWindow? OpenAt(Control anchor, Point at, Control body)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
            return null;
        PixelPoint corner = anchor.PointToScreen(at);
        var dropdown = new DropdownWindow(body);
        Thickness margin = Ui.Theme.MenuShadowMargin;
        body.Measure(Size.Infinity);
        double scale = owner.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(body.DesiredSize.Width * scale), (int)Math.Ceiling(body.DesiredSize.Height * scale));
        var position = new PixelPoint(corner.X, corner.Y);
        if (owner.Screens.ScreenFromPoint(corner)?.WorkingArea is { } screen)
            position = new PixelPoint(Math.Min(position.X, screen.Right - size.Width), Math.Min(position.Y, screen.Bottom - size.Height));
        dropdown.Position = new PixelPoint((int)Math.Round(position.X - margin.Left * scale), (int)Math.Round(position.Y - margin.Top * scale));
        dropdown.Show(owner);
        return dropdown;
    }

    /// <summary>
    /// Opens <paramref name="body"/> above <paramref name="anchor"/>, <paramref name="gap"/> over it, its left edge on the
    /// anchor's, or its right edge with <paramref name="right"/>; none when the anchor isn't in a window. For a footer.
    /// </summary>
    public static DropdownWindow? OpenAbove(Control anchor, Control body, double gap, bool right = false)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
            return null;
        PixelPoint corner = anchor.PointToScreen(new Point(right ? anchor.Bounds.Width : 0, 0));
        var dropdown = new DropdownWindow(body);
        Thickness margin = Ui.Theme.MenuShadowMargin;
        body.Measure(Size.Infinity);
        double scale = owner.RenderScaling;
        double x = right ? corner.X - (body.DesiredSize.Width + margin.Left) * scale : corner.X - margin.Left * scale;
        double y = corner.Y - (gap + body.DesiredSize.Height + margin.Top) * scale;
        dropdown.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
        dropdown.Show(owner);
        return dropdown;
    }

    /// <summary>
    /// Opens <paramref name="body"/> under <paramref name="anchor"/>, its right edge on the anchor's, <paramref name="gap"/>
    /// below it; none when the anchor isn't in a window.
    /// </summary>
    public static DropdownWindow? OpenUnder(Control anchor, Control body, double gap)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
            return null;
        PixelPoint corner = anchor.PointToScreen(new Point(anchor.Bounds.Width, anchor.Bounds.Height));
        var dropdown = new DropdownWindow(body);
        // Placed before it shows, from the size its content asks for, so it never appears elsewhere first.
        Thickness margin = Ui.Theme.MenuShadowMargin;
        body.Measure(Size.Infinity);
        double scale = owner.RenderScaling;
        double width = body.DesiredSize.Width + margin.Left + margin.Right;
        dropdown.Position = new PixelPoint((int)Math.Round(corner.X - (width - margin.Right) * scale),
            (int)Math.Round(corner.Y + (gap - margin.Top) * scale));
        dropdown.Show(owner);
        return dropdown;
    }
}
