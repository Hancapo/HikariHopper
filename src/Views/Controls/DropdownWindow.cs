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

    private DropdownWindow(Control body)
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
        Content = new Panel
        {
            Margin = Ui.Theme.MenuShadowMargin,
            Children = { new RetroMenuFrame { ConnectionWidth = 0 }, body },
        };
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
