using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Application-drawn 1px title-bar marks (the maximize and restore squares rounded like the window): minimize, maximize, restore and close
/// (port of <c>WindowControlGlyph.qml</c>). The 18x18 mark is centred in the control and steps
/// down by 1px while <see cref="Pressed"/>.
/// </summary>
public class WindowControlGlyph : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<WindowControlGlyph, string>(nameof(Kind), "");

    public static readonly StyledProperty<IBrush> InkProperty =
        AvaloniaProperty.Register<WindowControlGlyph, IBrush>(nameof(Ink), Ui.Theme.TextDim);

    public static readonly StyledProperty<bool> PressedProperty =
        AvaloniaProperty.Register<WindowControlGlyph, bool>(nameof(Pressed));

    private const double Box = 18;

    static WindowControlGlyph()
    {
        AffectsRender<WindowControlGlyph>(KindProperty, InkProperty, PressedProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<WindowControlGlyph>(AccessibilityView.Raw);
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public bool Pressed
    {
        get => GetValue(PressedProperty);
        set => SetValue(PressedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    public override void Render(DrawingContext context)
    {
        // anchors.centerIn snaps the 18x18 box to whole pixels (alignWhenCentered).
        double left = QtRound((Bounds.Width - Box) / 2);
        double top = QtRound((Bounds.Height - Box) / 2) + (Pressed ? 1 : 0);
        using (context.PushTransform(Matrix.CreateTranslation(left, top)))
        {
            IBrush ink = Ink;
            switch (Kind)
            {
                case "minimize":
                    // Centred on the same line as the other marks.
                    context.FillRectangle(ink, new Rect(4, 8, 10, 1));
                    break;
                case "maximize":
                    // The rounded square that matches the window's own corners.
                    context.DrawRectangle(null, new Pen(ink, 1), new Rect(4.5, 4.5, 9, 9), 2, 2);
                    break;
                case "restore":
                    context.DrawGeometry(null, new Pen(ink, 1), StreamGeometry.Parse(
                        "M5.5,6.5 L5.5,6 A1.5,1.5 0 0 1 7,4.5 L12,4.5 A1.5,1.5 0 0 1 13.5,6 L13.5,11 A1.5,1.5 0 0 1 12,12.5 L11.5,12.5"));
                    context.DrawRectangle(null, new Pen(ink, 1), new Rect(3.5, 6.5, 8, 8), 2, 2);
                    break;
                case "close":
                    Bar(context, ink, 45);
                    Bar(context, ink, -45);
                    break;
            }
        }
    }

    /// <summary>
    /// An 11x1 rectangle centred in the box and rotated about its own centre, non-antialiased like
    /// Qt's Rectangle. Centring snaps it to (3, 8), so it rotates about (8.5, 8.5).
    /// </summary>
    private static void Bar(DrawingContext context, IBrush ink, double degrees)
    {
        var rotation = Matrix.CreateTranslation(-8.5, -8.5)
            * Matrix.CreateRotation(degrees * Math.PI / 180)
            * Matrix.CreateTranslation(8.5, 8.5);
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
        using (context.PushTransform(rotation))
            context.FillRectangle(ink, new Rect(3, 8, 11, 1));
    }

    private static double QtRound(double value) => Math.Floor(value + 0.5);
}
