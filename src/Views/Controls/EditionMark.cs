using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The mark of a game tab: a flat slab-serif V, grey for GTA V Legacy and green for Enhanced.
/// </summary>
public sealed class EditionMark : Control
{
    public static readonly StyledProperty<bool> EnhancedProperty =
        AvaloniaProperty.Register<EditionMark, bool>(nameof(Enhanced));

    // A 16-unit V with serifs on both arms.
    private static readonly Geometry Shape = Geometry.Parse(
        "M0.8 2 H7.4 V3.5 H6.3 L8.7 10.4 L11.3 3.5 H10.2 V2 H15.2 V3.5 H14.1 L9.4 14.8 H7.1 L2.2 3.5 H0.8 Z");

    // The box it is fitted into, a little margin around the V.
    private static readonly Rect Frame = new(0.6, 1.8, 14.8, 13.2);

    static EditionMark()
    {
        AffectsRender<EditionMark>(EnhancedProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<EditionMark>(AccessibilityView.Raw);
    }

    /// <summary>True for GTA V Enhanced, false for Legacy.</summary>
    public bool Enhanced
    {
        get => GetValue(EnhancedProperty);
        set => SetValue(EnhancedProperty, value);
    }

    public IBrush Fill => Enhanced ? Ui.Theme.Selection : Ui.Theme.TextDim;

    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Width / Frame.Width, Bounds.Height / Frame.Height);
        double left = (Bounds.Width - Frame.Width * scale) / 2 - Frame.X * scale;
        double top = (Bounds.Height - Frame.Height * scale) / 2 - Frame.Y * scale;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
            context.DrawGeometry(Fill, null, Shape);
    }
}
