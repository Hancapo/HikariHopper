using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>Qt Quick anchor centring with <c>alignWhenCentered</c>.</summary>
public static class QtCenter
{
    /// <summary>An anchored item's centre: <c>(size + 1) / 2</c> when its whole-pixel size is odd.</summary>
    public static double Half(double size) => (int)size % 2 != 0 ? (size + 1) / 2 : size / 2;

    /// <summary>The position that centres <paramref name="child"/> in <paramref name="parent"/>.</summary>
    public static double Offset(double parent, double child) => Half(parent) - Half(child);

    /// <summary>A control's Qt implicit width: text is measured exactly, not rounded up.</summary>
    public static double ImplicitWidth(Control control) => control switch
    {
        TextBlock text => text.TextLayout.WidthIncludingTrailingWhitespace,
        StackPanel { Orientation: Avalonia.Layout.Orientation.Vertical } column =>
            column.Children.Where(child => child.IsVisible).Select(ImplicitWidth).DefaultIfEmpty(0).Max(),
        _ => control.DesiredSize.Width,
    };
}

/// <summary>Children placed with <c>anchors.centerIn: parent</c> (Qt's rounding, exact text widths).</summary>
public sealed class QtCenteredPanel : Panel
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            Size size = child.DesiredSize;
            double x = Math.Round(QtCenter.Offset(finalSize.Width, QtCenter.ImplicitWidth(child)));
            double y = Math.Round(QtCenter.Offset(finalSize.Height, size.Height));
            child.Arrange(new Rect(x, y, size.Width, size.Height));
        }
        return finalSize;
    }
}
