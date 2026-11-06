using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>A 4px track with an accent fill to <see cref="Value"/> (0 to 1), as the status bar shows indexing.</summary>
public sealed class ThinProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ThinProgress, double>(nameof(Value));

    static ThinProgress()
    {
        AffectsRender<ThinProgress>(ValueProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 110 : availableSize.Width, 4);

    public override void Render(DrawingContext context)
    {
        var track = new Rect(0, (Bounds.Height - 4) / 2, Bounds.Width, 4);
        context.DrawRectangle(Ui.Theme.ChromeRaised, null, track, 2, 2);
        double fill = Math.Clamp(Value, 0, 1) * Bounds.Width;
        if (fill > 0)
            context.DrawRectangle(Ui.Theme.Accent, null, track.WithWidth(Math.Max(4, fill)), 2, 2);
    }
}
