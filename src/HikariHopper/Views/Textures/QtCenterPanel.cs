using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Places each child at its desired size the way QML <c>anchors.centerIn: parent</c> does: Qt
/// measures a centre as half the size, except that an odd whole size takes the larger half
/// (<c>(size + 1) / 2</c>), and <c>alignWhenCentered</c> rounds the result to a whole pixel.
/// </summary>
public class QtCenterPanel : Panel
{
    /// <summary>QQuickAnchors' <c>hcenter</c>/<c>vcenter</c> of a length.</summary>
    public static double Center(double length) => (int)length % 2 != 0 ? (length + 1) / 2 : length / 2;

    /// <summary>The offset of a centred child of <paramref name="child"/> length inside <paramref name="parent"/>.</summary>
    public static double Offset(double parent, double child) => Math.Floor(Center(parent) - Center(child) + 0.5);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            Size size = child.DesiredSize;
            child.Arrange(new Rect(Offset(finalSize.Width, size.Width), Offset(finalSize.Height, size.Height), size.Width, size.Height));
        }
        return finalSize;
    }
}
