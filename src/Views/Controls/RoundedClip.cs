using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Clips its child to a rounded rectangle the size of its bounds, so whatever reaches the corners (a
/// header, a footer, a status bar with its own background) follows the card's curve instead of
/// leaving square corners over the blurred window.
/// </summary>
public sealed class RoundedClip : Decorator
{
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<RoundedClip, double>(nameof(Radius));

    static RoundedClip()
    {
        AffectsArrange<RoundedClip>(RadiusProperty);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        Clip = new RectangleGeometry(new Rect(arranged), Radius, Radius);
        return arranged;
    }
}
